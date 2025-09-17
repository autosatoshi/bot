// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// Suppress StyleCop rules that are not applicable to this project
[assembly: SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1633:File should have header", Justification = "Not required for this project")]
[assembly: SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1200:Using directives should be placed correctly", Justification = "Using file-scoped namespaces")]
[assembly: SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1101:Prefix local calls with this", Justification = "Not required for this project")]
[assembly: SuppressMessage("StyleCop.CSharp.NamingRules", "SA1309:Field names should not begin with underscore", Justification = "Underscore prefix is used for private fields")]
[assembly: SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "Documentation not required for internal APIs")]
[assembly: SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1601:Partial elements should be documented", Justification = "Documentation not required for internal APIs")]
[assembly: SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1602:Enumeration items should be documented", Justification = "Documentation not required for internal APIs")]
[assembly: SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:Element should begin with uppercase letter", Justification = "API models use snake_case naming from external API")]
[assembly: SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1133:Each attribute should be placed in its own set of square brackets", Justification = "Multiple attributes on single line is acceptable")]
[assembly: SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:A field should not follow a class", Justification = "Field ordering is acceptable for this pattern")]
[assembly: SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204:Static members should appear before non-static members", Justification = "Helper methods placed at end for readability")]
[assembly: SuppressMessage("StyleCop.CSharp.NamingRules", "SA1313:Parameter should begin with lower-case letter", Justification = "Primary constructor parameters use underscore prefix")]
