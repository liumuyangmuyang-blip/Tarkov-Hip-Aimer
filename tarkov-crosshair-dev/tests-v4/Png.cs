using System;
using System.IO;
using System.IO.Compression;

namespace TarkovCrosshair.Tests
{
    /// <summary>Tiny RGBA PNG writer (ZLibStream).</summary>
    public static class Png
    {
        private static readonly uint[] crcTable = MakeTable();

        private static uint[] MakeTable()
        {
            uint[] t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        private static uint Crc(byte[] type, byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in type) c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            foreach (byte b in data) c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
            WriteBE(s, (uint)data.Length);
            s.Write(t, 0, 4);
            s.Write(data, 0, data.Length);
            WriteBE(s, Crc(t, data));
        }

        private static void WriteBE(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
        }

        /// <summary>rgba: width*height*4 bytes, non-premultiplied RGBA.</summary>
        public static void Write(string path, int width, int height, byte[] rgba)
        {
            using (FileStream fs = File.Create(path))
            {
                fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                MemoryStream ih = new MemoryStream();
                WriteBE(ih, (uint)width); WriteBE(ih, (uint)height);
                ih.WriteByte(8); ih.WriteByte(6); ih.WriteByte(0); ih.WriteByte(0); ih.WriteByte(0);
                Chunk(fs, "IHDR", ih.ToArray());
                MemoryStream z = new MemoryStream();
                using (ZLibStream zs = new ZLibStream(z, CompressionLevel.Optimal, true))
                {
                    for (int y = 0; y < height; y++)
                    {
                        zs.WriteByte(0);
                        zs.Write(rgba, y * width * 4, width * 4);
                    }
                }
                Chunk(fs, "IDAT", z.ToArray());
                Chunk(fs, "IEND", new byte[0]);
            }
        }
    }
}
