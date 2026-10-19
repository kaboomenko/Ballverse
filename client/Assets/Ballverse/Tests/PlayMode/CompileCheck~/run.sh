#!/usr/bin/env bash
# Compile check of the PlayMode smoke test (Ballverse.PlayModeTests) under plain .NET 8, no Unity:
#   1. the hand-copied signature stand-ins still match the real sources (UI runtime, Ballverse.Editor.ProjectSetup);
#   2. the editor branch (UNITY_EDITOR) and the player branch compile against the real App / Sim / Studio / Media / Services /
#      Render / UI model sources and Core (warnings are errors).
# GameCI's Unity compile remains the authority. Usage: ./run.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
ballverse="$(cd "$here/../../.." && pwd)"

check() { # file, exact line fragment
  if ! grep -qF -- "$2" "$ballverse/$1"; then
    echo "Stale stand-in: '$2' not found in $1 (update CompileCheck~/Stubs)" >&2
    exit 1
  fi
}
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
echo "Stand-in signatures match the real sources."

dotnet build "$here/Ballverse.PlayModeTests.CompileCheck.csproj" -c Release -p:SmokeEditor=true
dotnet build "$here/Ballverse.PlayModeTests.CompileCheck.csproj" -c Release -p:SmokeEditor=false
