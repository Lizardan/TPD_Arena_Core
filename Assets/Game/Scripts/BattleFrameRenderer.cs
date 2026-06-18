using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TPD.Arena
{
    public class BattleFrameRenderer
    {
        private readonly BattleReplayer replayer;
        private readonly Slider timelineSlider;
        private readonly int p1MaxHealth;
        private readonly int p2MaxHealth;
        private readonly List<TimelineEvent> timelineEvents;

        private bool exportSessionActive;
        private float lastAppliedTime = -1f;
        private BattleExportFrameCapture offscreenCapture;

        public BattleFrameRenderer(
            BattleReplayer replayer,
            Slider timelineSlider,
            List<TimelineEvent> timelineEvents,
            int maxHealth)
            : this(replayer, timelineSlider, timelineEvents, maxHealth, maxHealth)
        {
        }

        public BattleFrameRenderer(
            BattleReplayer replayer,
            Slider timelineSlider,
            List<TimelineEvent> timelineEvents,
            int p1MaxHealth,
            int p2MaxHealth)
        {
            this.replayer = replayer;
            this.timelineSlider = timelineSlider;
            this.timelineEvents = timelineEvents;
            this.p1MaxHealth = p1MaxHealth;
            this.p2MaxHealth = p2MaxHealth;
        }

        public bool BeginExport(Camera captureCamera, int width, int height)
        {
            exportSessionActive = true;
            lastAppliedTime = -1f;
            offscreenCapture = new BattleExportFrameCapture();
            return offscreenCapture.Begin(captureCamera, width, height);
        }

        public void EndExport()
        {
            offscreenCapture?.End();
            offscreenCapture = null;
            exportSessionActive = false;
            lastAppliedTime = -1f;
        }

        public void ApplyFrame(float time, bool updateTimelineSlider = true)
        {
            const float epsilon = 0.001f;
            if (!exportSessionActive || lastAppliedTime < 0f || time < lastAppliedTime - epsilon)
                replayer.Initialize(timelineEvents, p1MaxHealth, p2MaxHealth);

            lastAppliedTime = time;
            replayer.UpdateAtTime(time, playTransientVfx: false);

            if (updateTimelineSlider && timelineSlider != null)
                timelineSlider.SetValueWithoutNotify(time);

            Canvas.ForceUpdateCanvases();
        }

        public Texture2D CaptureFrame()
        {
            replayer.PrepareSkinnedMeshesForRender();
            return offscreenCapture != null ? offscreenCapture.Capture() : null;
        }
    }
}
