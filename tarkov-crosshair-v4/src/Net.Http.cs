// v4.4 (W9) NET: the real IHttp (HttpWebRequest). TLS 1.2 forced, User-Agent set, short timeouts, never throws.
// Call it from a background thread only (never the UI or overlay thread). Not compiled into the net8 tests (fakes there).
using System;
using System.IO;
using System.Net;
using System.Text;

namespace TarkovCrosshair
{
    internal sealed class NetHttp : IHttp
    {
        private static bool tlsSet;
        private static readonly object tlsLock = new object();

        /// <summary>.NET Framework 4.x defaults can leave TLS 1.2 off; GitHub and Cloudflare need it.</summary>
        internal static void EnsureTls12()
        {
            lock (tlsLock)
            {
                if (tlsSet) return;
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch (Exception) { }
                tlsSet = true;
            }
        }

        private static HttpWebRequest Make(string url, string method, int timeoutMs)
        {
            EnsureTls12();
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.Method = method;
            r.UserAgent = NetConfig.UserAgent;
            r.Timeout = timeoutMs;
            r.ReadWriteTimeout = timeoutMs;
            r.AllowAutoRedirect = true;
            r.MaximumAutomaticRedirections = 5;
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            r.Accept = url.StartsWith("https://api.github.com/", StringComparison.Ordinal) ? "application/vnd.github+json" : "application/json";
            return r;
        }

        private static HttpResult Read(HttpWebRequest r)
        {
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                    return HttpResult.Of((int)resp.StatusCode, ReadBody(resp, 4 * 1024 * 1024));
            }
            catch (WebException we)
            {
                HttpWebResponse resp = we.Response as HttpWebResponse;
                if (resp != null)
                {
                    using (resp)
                    {
                        string body = null;
                        try { body = ReadBody(resp, 64 * 1024); } catch (Exception) { }
                        HttpResult h = HttpResult.Of((int)resp.StatusCode, body);
                        h.Error = we.Status.ToString();
                        return h;
                    }
                }
                return HttpResult.Fail(we.Status.ToString());
            }
            catch (Exception e) { return HttpResult.Fail(e.GetType().Name); }
        }

        private static string ReadBody(HttpWebResponse resp, int maxChars)
        {
            using (Stream s = resp.GetResponseStream())
            using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
            {
                char[] buf = new char[8192];
                StringBuilder b = new StringBuilder();
                int n;
                while ((n = sr.Read(buf, 0, buf.Length)) > 0)
                {
                    b.Append(buf, 0, n);
                    if (b.Length > maxChars) throw new IOException("reply too large");
                }
                return b.ToString();
            }
        }

        public HttpResult Get(string url, int timeoutMs)
        {
            try { return Read(Make(url, "GET", timeoutMs)); }
            catch (Exception e) { return HttpResult.Fail(e.GetType().Name); }
        }

        public HttpResult PostJson(string url, string json, int timeoutMs)
        {
            try
            {
                HttpWebRequest r = Make(url, "POST", timeoutMs);
                byte[] data = Encoding.UTF8.GetBytes(json ?? "");
                r.ContentType = "application/json";
                r.ContentLength = data.Length;
                using (Stream s = r.GetRequestStream()) s.Write(data, 0, data.Length);
                return Read(r);
            }
            catch (Exception e) { return HttpResult.Fail(e.GetType().Name); }
        }

        public bool Download(string url, string path, int timeoutMs, long maxBytes, out string error)
        {
            error = null;
            try
            {
                HttpWebRequest r = Make(url, "GET", timeoutMs);
                r.Accept = "application/octet-stream";
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                {
                    if ((int)resp.StatusCode < 200 || (int)resp.StatusCode >= 300) { error = "HTTP " + (int)resp.StatusCode; return false; }
                    if (resp.ContentLength > maxBytes) { error = "too large"; return false; }
                    long total = 0;
                    using (Stream s = resp.GetResponseStream())
                    using (FileStream f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buf = new byte[81920];
                        int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                        {
                            total += n;
                            if (total > maxBytes) { error = "too large"; break; }
                            f.Write(buf, 0, n);
                        }
                    }
                    if (error == null) return true;
                }
            }
            catch (WebException we) { error = we.Status.ToString(); }
            catch (Exception e) { error = e.GetType().Name; }
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
            return false;
        }
    }
}
