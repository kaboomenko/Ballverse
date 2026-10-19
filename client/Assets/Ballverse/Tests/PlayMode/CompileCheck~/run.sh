#!/usr/bin/env bash
# Checks of the PlayMode smoke test (Ballverse.PlayModeTests) under plain .NET 8, no Unity:
#   1. the hand-copied signature stand-ins still match the real sources (the UI shell stand-ins shared with the App harness,
#      Tests/AppHarness~/Stubs/UiRuntimeStubs.cs, plus the navigator members and Ballverse.Editor.ProjectSetup in
#      Stubs/PlayModeStubs.cs), and so does the copied list of
#      renderer shaders (SmokeLog.cs ExpectedLogs.RendererShaders vs Render/Runtime/RenderResources.cs);
#   2. the editor branch (UNITY_EDITOR) and the player branch compile against the real App / Sim / Studio / Media / Services /
#      Render / UI model sources and Core (warnings are errors);
#   3. LogicTests: the smoke test's engine-free rules (🎲 post-condition against the real view models, log classification,
#      whitelist) under NUnit.
# GameCI's Unity compile remains the authority. Usage: ./run.sh [extra dotnet test args]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
ballverse="$(cd "$here/../../.." && pwd)"

check() { # file, exact line fragment
  if ! grep -qF -- "$2" "$ballverse/$1"; then
    echo "Stale stand-in: '$2' not found in $1 (update CompileCheck~/Stubs)" >&2
    exit 1
  fi
}
# UI shell stand-ins: Tests/AppHarness~/Stubs/UiRuntimeStubs.cs (shared with the App harness) + Stubs/PlayModeStubs.cs.
check UI/Runtime/Shell/BallverseUIRoot.cs 'public UiShell Shell { get; private set; }'
check UI/Runtime/Shell/BallverseUIRoot.cs 'public static BallverseUIRoot Create(IBallverseViewModels viewModels, ISafeAreaProvider safeArea, int sortingOrder = 0,'
check UI/Runtime/Shell/UiShell.cs 'public UiNavigator Navigator => _ctx.Navigator;'
check UI/Runtime/Shell/UiShell.cs 'public DialogHost Dialogs => _ctx.Dialogs;'
check UI/Runtime/Shell/UiShell.cs 'public void OpenMode(string modeId)'
check UI/Runtime/Shell/UiShell.cs 'public void ShowAdNotice(Action shown)'
check UI/Runtime/Shell/UiShell.cs 'public void ShowTrackingPrePrompt()'
check UI/Runtime/Shell/UiShell.cs 'public void ShowPhotosensitivityWarning(Action acknowledged)'
check UI/Runtime/Shell/UiShell.cs 'public void ShowToast(LocalizedText text)'
check UI/Runtime/Shell/UiShell.cs 'public void SetDeviceTier(DeviceTier tier)'
check UI/Runtime/Navigation/UiNavigator.cs 'public event Action<ScreenId> ScreenChanged;'
check UI/Runtime/Navigation/UiNavigator.cs 'public event Action ExitRequested;'
check UI/Runtime/Navigation/UiNavigator.cs 'public ScreenId Current => _stack.Top;'
check UI/Runtime/Navigation/UiNavigator.cs 'public NavigationStack Stack => _stack;'
check UI/Runtime/Navigation/UiNavigator.cs 'public void Push(ScreenId id)'
check UI/Runtime/Navigation/UiNavigator.cs 'public void Pop()'
check UI/Runtime/Navigation/UiNavigator.cs 'public void SelectTab(TabId tab)'
check UI/Runtime/Navigation/UiNavigator.cs 'public BackOutcome Back()'
check UI/Runtime/Components/DialogHost.cs 'public DialogButton(LocalizedText label, DialogButtonStyle style, Action<string> action, bool keepOpen = false)'
check UI/Runtime/Components/DialogHost.cs 'public DialogHandle Show(DialogSpec spec)'
check UI/Runtime/Components/DialogHost.cs 'public bool IsOpen =>'
check UI/Runtime/Localization/ResourcesTableSource.cs 'public static string SystemLanguageTag()'
check Editor/ProjectSetup.cs 'public const string ScenesFolder = "Assets/Scenes";'
check Editor/ProjectSetup.cs 'public const string BootScenePath = ScenesFolder + "/Boot.unity";'
check Editor/ProjectSetup.cs 'public static void Run()'
# ExpectedLogs.RendererShaders (SmokeLog.cs) copies RenderResources' (Resources name, Shader.Find name) pairs.
check Render/Runtime/RenderResources.cs 'CreateMaterial("BallverseBatch", "Hidden/Ballverse/Batch", false);'
check Render/Runtime/RenderResources.cs 'CreateInstancedMaterial(BallTemplate, "BallverseSdfBall", BallShaderName, out bool ballTemplate);'
check Render/Runtime/RenderResources.cs 'public const string BallShaderName = "Hidden/Ballverse/SdfBall";'
check Render/Runtime/RenderResources.cs 'CreateInstancedMaterial(ParticleTemplate, "BallverseParticle", ParticleShaderName, out bool particleTemplate);'
check Render/Runtime/RenderResources.cs 'public const string ParticleShaderName = "Hidden/Ballverse/Particle";'
check Render/Runtime/RenderResources.cs 'CreateMaterial("BallverseText", "Hidden/Ballverse/Text", false);'
check Render/Runtime/RenderResources.cs 'CreateMaterial("BallverseBackground", "Hidden/Ballverse/Background", false);'
check Render/Runtime/RenderResources.cs 'CreateMaterial("BallversePost", "Hidden/Ballverse/Post", false);'
materials="$(grep -cE 'Material = Create(Instanced)?Material\(' "$ballverse/Render/Runtime/RenderResources.cs")"
if [ "$materials" != "6" ]; then
  echo "RenderResources creates $materials materials, SmokeLog.cs ExpectedLogs.RendererShaders lists 6: update the list" >&2
  exit 1
fi
echo "Stand-in signatures match the real sources."

dotnet build "$here/Ballverse.PlayModeTests.CompileCheck.csproj" -c Release -p:SmokeEditor=true
dotnet build "$here/Ballverse.PlayModeTests.CompileCheck.csproj" -c Release -p:SmokeEditor=false
dotnet test "$here/LogicTests/Ballverse.PlayModeTests.LogicTests.csproj" -c Release "$@"
