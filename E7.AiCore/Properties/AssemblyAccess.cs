// E7MRIWeb (E7MRIV2Web.csproj) calls engine members that stay internal (Shared AI Core 2e).
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("E7MRIWeb")]
// FRS advance (E7FRSAdvance web project) calls the same internal roster members through its MaintenceRosterController port.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("E7FRSAdvance")]
