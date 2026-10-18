using System.Runtime.CompilerServices;
using Unity.Entities;

[assembly: DisableAutoCreation]

// The module suites build on the session and frame helpers in Support.
[assembly: InternalsVisibleTo("Pragma.Dots.Navigation.Tests.Editor")]
[assembly: InternalsVisibleTo("Pragma.Dots.Stats.Tests.Editor")]
[assembly: InternalsVisibleTo("Pragma.Dots.Views.Tests.Editor")]
[assembly: InternalsVisibleTo("Pragma.Dots.Vision.Tests.Editor")]
