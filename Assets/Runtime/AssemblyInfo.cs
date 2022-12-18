using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Pixelism.Runtime.Tests")]
#if UNITY_EDITOR
[assembly: InternalsVisibleTo("Pixelism.Editor.Tests")]
#endif
