using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "I don't care.")]

[assembly: SuppressMessage("Major Code Smell", "S3881:\"IDisposable\" should be implemented correctly", Justification = "Spurious incorrectness or just incapable of clearly stating the flaw.")]

[assembly: SuppressMessage("Major Code Smell", "S1121:Assignments should not be made from within sub-expressions", Justification = "Clearer for me this way.")]

[assembly: SuppressMessage("Major Code Smell", "S3358:Ternary operators should not be nested", Justification = "Don't care")]

[assembly: SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Unnecessary.")]
[assembly: SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "Text in resource file is such a bother.")]
[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "What?")]
[assembly: SuppressMessage("Naming", "CA1717:Only FlagsAttribute enums should have plural names", Justification = "Couldn't care less")]
[assembly: SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "I need those objects.")]
[assembly: SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "I dooon't caaaaaare")]
[assembly: SuppressMessage("Performance", "CA1806:Do not ignore method results", Justification = "I don't CAAAARE")]

[assembly: SuppressMessage("CodeQuality", "IDE0052:Remove unread private members", Justification = "Double warning")]

[assembly: SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Dooon't caaaare")]
[assembly: SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "<Pending>", Scope = "member", Target = "~P:MKAh.Ini.Config.CommentChars")]
[assembly: SuppressMessage("Performance", "CA1805:Do not initialize unnecessarily", Justification = "<Pending>", Scope = "member", Target = "~F:MKAh.Ini.Config.Strict")]
