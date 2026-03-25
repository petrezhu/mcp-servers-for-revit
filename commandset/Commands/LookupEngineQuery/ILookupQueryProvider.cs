using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LookupEngine;
using LookupEngine.Abstractions;
using LookupEngine.Abstractions.Enums;
using RevitLookup.Core.Decomposition;

namespace RevitMCPCommandSet.Commands.LookupEngineQuery
{
    internal interface ILookupQueryProvider
    {
        string LookupEngineVersion { get; }
        string RevitLookupVersion { get; }
        bool UsesDescriptorsMap { get; }
        IReadOnlyList<string> Diagnostics { get; }

        bool TryGetMembers(Type type, int maxMembers, out List<string> members, out string errorMessage);
    }

    internal static class LookupQueryProviderFactory
    {
        public static bool TryCreate(out ILookupQueryProvider provider, out string errorMessage)
        {
            try
            {
                provider = new LookupEngineProvider();
                errorMessage = null;
                return true;
            }
            catch (FileNotFoundException ex)
            {
                provider = null;
                errorMessage = $"LookupEngine dependency is missing: {ex.FileName ?? ex.Message}";
                return false;
            }
            catch (FileLoadException ex)
            {
                provider = null;
                errorMessage = $"LookupEngine dependency failed to load: {ex.Message}";
                return false;
            }
            catch (BadImageFormatException ex)
            {
                provider = null;
                errorMessage = $"LookupEngine dependency has an incompatible architecture or target framework: {ex.Message}";
                return false;
            }
            catch (TypeLoadException ex)
            {
                provider = null;
                errorMessage = $"LookupEngine or RevitLookup version is incompatible: {ex.Message}";
                return false;
            }
            catch (MissingMethodException ex)
            {
                provider = null;
                errorMessage = $"LookupEngine or RevitLookup API is incompatible: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                provider = null;
                errorMessage = Unwrap(ex).Message;
                return false;
            }
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException tie && tie.InnerException != null)
            {
                ex = tie.InnerException;
            }

            return ex;
        }
    }

    internal sealed class LookupEngineProvider : ILookupQueryProvider
    {
        private readonly List<string> _diagnostics = new List<string>();
        private readonly bool _descriptorsAvailable;

        public LookupEngineProvider()
        {
            LookupEngineVersion = typeof(LookupComposer).Assembly.GetName().Version?.ToString();
            RevitLookupVersion = typeof(DescriptorsMap).Assembly.GetName().Version?.ToString();
            _descriptorsAvailable = ValidateDescriptorsMap(out var descriptorWarning);

            if (!string.IsNullOrWhiteSpace(descriptorWarning))
            {
                _diagnostics.Add(descriptorWarning);
            }
        }

        public string LookupEngineVersion { get; }

        public string RevitLookupVersion { get; }

        public bool UsesDescriptorsMap => _descriptorsAvailable;

        public IReadOnlyList<string> Diagnostics => _diagnostics;

        public bool TryGetMembers(Type type, int maxMembers, out List<string> members, out string errorMessage)
        {
            members = new List<string>();
            errorMessage = null;

            try
            {
                var options = CreateOptions();
                var rawMembers = LookupComposer.DecomposeMembers(type, options);

                members = rawMembers
                    .Select(FormatMemberSignature)
                    .Where(signature => !string.IsNullOrWhiteSpace(signature))
                    .Distinct(StringComparer.Ordinal)
                    .Take(maxMembers)
                    .ToList();

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = Unwrap(ex).Message;
                return false;
            }
        }

        private DecomposeOptions CreateOptions()
        {
            var options = new DecomposeOptions
            {
                IncludeRoot = false,
                IncludeFields = true,
                IncludeEvents = false,
                IncludeUnsupported = true,
                IncludePrivateMembers = false,
                IncludeStaticMembers = true,
                EnableExtensions = true,
                EnableRedirection = true
            };

            if (_descriptorsAvailable)
            {
                options.TypeResolver = DescriptorsMap.FindDescriptor;
            }

            return options;
        }

        private static bool ValidateDescriptorsMap(out string warningMessage)
        {
            try
            {
                var descriptor = DescriptorsMap.FindDescriptor(null, typeof(object));
                if (descriptor == null)
                {
                    warningMessage = "RevitLookup descriptors are unavailable. LookupEngine will run with its default resolver.";
                    return false;
                }

                warningMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                warningMessage = $"RevitLookup descriptors are unavailable. LookupEngine will run with its default resolver. {Unwrap(ex).Message}";
                return false;
            }
        }

        private static string FormatMemberSignature(DecomposedMember member)
        {
            if (member == null || string.IsNullOrWhiteSpace(member.Name))
            {
                return null;
            }

            string kind = ResolveMemberKind(member.MemberAttributes);
            string valueType = member.Value?.TypeName;

            if (!string.IsNullOrWhiteSpace(valueType))
            {
                return $"{kind}:{member.Name}->{valueType}";
            }

            return $"{kind}:{member.Name}";
        }

        private static string ResolveMemberKind(MemberAttributes attributes)
        {
            if ((attributes & MemberAttributes.Property) == MemberAttributes.Property)
            {
                return "Property";
            }

            if ((attributes & MemberAttributes.Method) == MemberAttributes.Method)
            {
                return "Method";
            }

            if ((attributes & MemberAttributes.Field) == MemberAttributes.Field)
            {
                return "Field";
            }

            if ((attributes & MemberAttributes.Event) == MemberAttributes.Event)
            {
                return "Event";
            }

            if ((attributes & MemberAttributes.Extension) == MemberAttributes.Extension)
            {
                return "Extension";
            }

            return "Member";
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException tie && tie.InnerException != null)
            {
                ex = tie.InnerException;
            }

            return ex;
        }
    }
}
