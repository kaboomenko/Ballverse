using System.Runtime.CompilerServices;

// EditMode tests drive shell internals (tool sheets, contexts) without a running app.
[assembly: InternalsVisibleTo("Ballverse.UI.Tests")]
// Fakes share small engine helpers (UnityObjects).
[assembly: InternalsVisibleTo("Ballverse.UI.Fakes")]
