using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("Taskmaster!")]
[assembly: AssemblyDescription("Maintenance and sanitation of unruly minions.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("Taskmaster!")]
[assembly: AssemblyCopyright("Copyright © M.A., 2016–2021")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

[assembly: AssemblyVersion("0.15.*")] // FIXME: Get this somehow from the solution settings?
#pragma warning disable CS7035 // a.b.c.d versioning warning
[assembly: AssemblyFileVersion("0.15.*")]
[assembly: System.Resources.NeutralResourcesLanguage("en")]
[assembly: Guid("088f7210-51b2-4e06-9bd4-93c27a973874")] // this is pointless, yes?

// for unit tests
[assembly: InternalsVisibleTo("TaskmasterUnitTests")]
