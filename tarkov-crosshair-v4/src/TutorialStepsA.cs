// TutorialStepsA.cs -- 首次设置教程 (v4.0) page order for script steps 1-7 (v4-tutorial-script.md).
// Steps 1-2 live here (TUTORIAL); steps 3-7 are one file each (TutorialStep3.cs .. TutorialStep7.cs, one worker each).
// System.Drawing only (no WinForms).
using System;
using System.Collections.Generic;
using System.Drawing;

namespace TarkovCrosshair
{
    public static class TutorialStepsA
    {
        public static List<TutorialStep> Create()
        {
            List<TutorialStep> l = new List<TutorialStep>();
            l.Add(new TutorialStepWhy());
            l.Add(new TutorialStepHipfire());
            TutorialStep3.Add(l);
            TutorialStep4.Add(l);
            TutorialStep5.Add(l);
            TutorialStep6.Add(l);
            TutorialStep7.Add(l);
            TutorialStepsV51.AddCalibration(l);   // v5.1: 站立静止偏移, 短按与反向 (TutorialStepsV51.cs)
            return l;
        }
    }

    // Steps 1-2 are implemented in TutorialSteps12.cs.
}
