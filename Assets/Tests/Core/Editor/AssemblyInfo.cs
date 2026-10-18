using System.Runtime.CompilerServices;
using Unity.Entities;

[assembly: DisableAutoCreation]

// The module suites build on the session and frame helpers in Support.
[assembly: InternalsVisibleTo("Pragma.Lockstep.Navigation.Tests.Editor")]
[assembly: InternalsVisibleTo("Pragma.Lockstep.Stats.Tests.Editor")]
[assembly: InternalsVisibleTo("Pragma.Lockstep.Views.Tests.Editor")]
[assembly: InternalsVisibleTo("Pragma.Lockstep.Vision.Tests.Editor")]
