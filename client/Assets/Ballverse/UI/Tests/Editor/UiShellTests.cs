using System.Collections;
using Ballverse.UI.Components;
using Ballverse.UI.Fakes;
using Ballverse.UI.Localization;
using Ballverse.UI.Navigation;
using Ballverse.UI.Screens;
using Ballverse.UI.ViewModels;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ballverse.UI.EditorTests
{
    /// <summary>
    /// EditMode tests of the shell on the fake view models (spec §8: EditMode tests in batchmode). Most run on a detached
    /// element tree (no panel: tweens complete at once, layout is not computed); the layout test hosts the shell in an
    /// editor window and is inconclusive when the editor cannot lay out windows (headless batchmode).
    /// </summary>
    public sealed class UiShellTests
    {
        FakeViewModels _vms;
        UiShell _shell;
        VisualElement _host;

        [SetUp]
        public void SetUp()
        {
            _vms = new FakeViewModels();
            _host = new VisualElement();
            _shell = new UiShell(_host, _vms);
            _vms.SetLocalizer(_shell.Localizer.Get);
        }

        [TearDown]
        public void TearDown()
        {
            _shell?.Dispose();
            _vms?.Dispose();
            Tween.ReduceMotion = false;
        }

        [Test]
        public void ShellContainerFillsTheHostWithInlineStyles()
        {
            // Blocker: the container is above app-root (which carries the style sheets), so USS cannot size it.
            Assert.AreEqual(1, _host.childCount);
            var container = _host[0];
            Assert.AreEqual(Position.Absolute, container.style.position.value);
            Assert.AreEqual(0f, container.style.left.value.value);
            Assert.AreEqual(0f, container.style.top.value.value);
            Assert.AreEqual(0f, container.style.right.value.value);
            Assert.AreEqual(0f, container.style.bottom.value.value);
            Assert.IsNotNull(container.Q<VisualElement>("app-root"));
        }

        [Test]
        public void LayoutKeysAreLocalized()
        {
            var label = _host.Q<VisualElement>("tab-modes").Q<Label>();
            Assert.AreEqual(_shell.Localizer.Get("tab.modes"), label.text);
        }

        [Test]
        public void SheetsAndDialogsKeepHashtagUserText()
        {
            // Blocker: Prepare used to treat "#…" in user content as keys and wrote the stand-in back.
            string committed = null;
            var sheet = new BottomSheet(_shell.Context, new SheetOptions());
            var form = new FormBuilder(_shell.Context, sheet.Content);
            var field = form.Text(null, "#satisfying", 200, true, v => committed = v);
            var caption = UiContext.TextLabel("#ballverse #satisfying");
            sheet.Content.Add(caption);
            sheet.Show();
            Assert.AreEqual("#satisfying", field.value);
            Assert.AreEqual("#ballverse #satisfying", caption.text);
            Assert.IsNull(committed);

            _shell.Dialogs.Show(new DialogSpec { Title = LocalizedText.FromLiteral("#1 Red"), TextFieldValue = "#1 Red" });
            var dialogLayer = _host.Q<VisualElement>("dialog-layer");
            Assert.AreEqual("#1 Red", dialogLayer.Q<Label>(className: "bv-dialog__title").text);
            Assert.AreEqual("#1 Red", dialogLayer.Q<TextField>().value);
            foreach (string key in _shell.Localizer.MissingKeys)
                StringAssert.DoesNotContain("satisfying", key);
            CollectionAssert.DoesNotContain(_shell.Localizer.MissingKeys, "1 Red");
        }

        StudioScreen OpenStudio()
        {
            _shell.Navigator.Push(ScreenId.Simulation);
            _shell.Navigator.Push(ScreenId.Studio);
            var studio = _shell.Navigator.CurrentController as StudioScreen;
            Assert.IsNotNull(studio);
            return studio;
        }

        [Test]
        public void SwitchingToolsSurvivesTheOldSheetsLateClosedCallback()
        {
            var studio = OpenStudio();
            studio.OpenTool(StudioTool.Layout);
            var layoutSheet = studio.ActiveToolSheet;
            Assert.IsNotNull(layoutSheet);
            studio.OpenTool(StudioTool.Video);
            var videoSheet = studio.ActiveToolSheet;
            Assert.AreNotSame(layoutSheet, videoSheet);
            // On a device the old sheet's Closed runs 0.22 s later, after the new sheet opened.
            layoutSheet.Options.Closed?.Invoke();
            Assert.AreEqual(StudioTool.Video, studio.ActiveTool);
            Assert.AreSame(videoSheet, studio.ActiveToolSheet);
            Assert.IsTrue(videoSheet.IsOpen);
            Assert.IsTrue(studio.Root.Q<VisualElement>("tool-video").ClassListContains("bv-tool-tab--selected"));
            // Leaving the studio closes the sheet that is really open.
            studio.CloseTool();
            Assert.IsFalse(videoSheet.IsOpen);
            Assert.IsNull(studio.ActiveTool);
        }

        [Test]
        public void TextToolKeepsAHashtagLayer()
        {
            var studio = OpenStudio();
            var text = _vms.Studio.Text;
            text.AddEmpty();
            text.SetText(0, "#satisfying");
            text.Select(0);
            studio.OpenTool(StudioTool.Text);
            Assert.AreEqual("#satisfying", text.Layers[0].Text);
            Assert.IsNotNull(studio.ActiveToolSheet.Content.Q<TextField>());
            Assert.AreEqual("#satisfying", studio.ActiveToolSheet.Content.Q<TextField>().value);
        }

        [Test]
        public void BackClosesDialogThenSheetThenScreen()
        {
            var studio = OpenStudio();
            studio.OpenTool(StudioTool.Sound);
            _shell.Dialogs.Confirm("export.cancel_title", null, "common.ok", false, null);
            Assert.AreEqual(BackOutcome.ClosedDialog, _shell.Navigator.Back());
            Assert.AreEqual(BackOutcome.ClosedSheet, _shell.Navigator.Back());
            Assert.AreEqual(ScreenId.Studio, _shell.Navigator.Current);
        }

        [Test]
        public void ExportStagesArePresented()
        {
            _vms.Export.Begin();
            Assert.IsTrue(_shell.Sheets.AnyOpen, "export settings sheet");
            _vms.Export.Close();
            _vms.SimulateInterruptedExport(0.4f);
            Assert.IsTrue(_shell.Dialogs.AnyOpen, "resume offer");
            _vms.Export.DiscardInterrupted();
            Assert.IsFalse(_shell.Dialogs.AnyOpen);
        }

        [Test]
        public void DisposeUnbindsTheVisibleScreen()
        {
            _shell.Navigator.Push(ScreenId.Simulation);
            var sim = _shell.Navigator.CurrentController;
            Assert.IsTrue(sim.IsVisible);
            _shell.Dispose();
            Assert.IsFalse(sim.IsVisible);
            _shell = null;
        }

        sealed class HostWindow : EditorWindow
        {
        }

        [UnityTest]
        public IEnumerator ShellTakesTheWholeWindow()
        {
            var window = ScriptableObject.CreateInstance<HostWindow>();
            window.position = new Rect(0, 0, 390, 844);
            window.Show();
            var vms = new FakeViewModels();
            var shell = new UiShell(window.rootVisualElement, vms);
            try
            {
                for (int i = 0; i < 20 && !(window.rootVisualElement.layout.height > 0f); i++)
                {
                    window.Repaint();
                    yield return null;
                }
                if (!(window.rootVisualElement.layout.height > 0f))
                {
                    Assert.Inconclusive("The editor did not lay out the test window (headless editor).");
                }
                for (int i = 0; i < 5; i++)
                {
                    window.Repaint();
                    yield return null;
                }
                var appRoot = window.rootVisualElement.Q<VisualElement>("app-root");
                Assert.Greater(appRoot.layout.height, 0f);
                Assert.AreEqual(window.rootVisualElement.layout.height, appRoot.layout.height, 1f);
                Assert.Greater(window.rootVisualElement.Q<VisualElement>("screen-layer").layout.height, 0f);
            }
            finally
            {
                shell.Dispose();
                vms.Dispose();
                window.Close();
            }
        }
    }
}
