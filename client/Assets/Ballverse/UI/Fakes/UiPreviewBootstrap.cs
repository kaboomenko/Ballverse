using UnityEngine;

namespace Ballverse.UI.Fakes
{
    /// <summary>
    /// Drop on any GameObject in an empty scene and press Play: the full app shell runs on <see cref="FakeViewModels"/>
    /// (simulation, studio, export, projects, shop and settings with timed fake behaviour).
    /// </summary>
    [AddComponentMenu("Ballverse/UI Preview (fake view models)")]
    public sealed class UiPreviewBootstrap : MonoBehaviour
    {
        [Tooltip("Open this mode's simulation on start, like the first launch (spec §4.3). Empty = gallery.")]
        [SerializeField] string _openModeOnStart = "ring_escape";
        [SerializeField] Core.DeviceTier _tier = Core.DeviceTier.B;

        FakeViewModels _vms;
        BallverseUIRoot _root;

        void Start()
        {
            _vms = new FakeViewModels();
            _root = BallverseUIRoot.Create(_vms, 0, transform);
            _vms.SetLocalizer(_root.Shell.Localizer.Get);
            _root.Shell.SetDeviceTier(_tier);
            if (!string.IsNullOrEmpty(_openModeOnStart)) _root.Shell.OpenMode(_openModeOnStart);
        }

        void Update()
        {
            _vms?.Tick(Time.unscaledDeltaTime);
        }

        void OnDestroy()
        {
            _vms?.Dispose();
        }
    }
}
