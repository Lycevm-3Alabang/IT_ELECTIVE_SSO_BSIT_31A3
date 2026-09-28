using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Gateway.Areas.Admin.Models.TenantApps
{
    /// <summary>Absolute http/https URL with a host. Server + unobtrusive client validation.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class HttpUrlAttribute : ValidationAttribute, IClientModelValidator
    {
        public HttpUrlAttribute() : base("Enter a valid URL starting with http:// or https://.") { }

        public override bool IsValid(object? value)
        {
            if (value is null || (value is string empty && empty.Length == 0))
            {
                return true; // [Required] handles emptiness
            }

            return value is string s
                && Uri.TryCreate(s.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrEmpty(uri.Host);
        }

        public void AddValidation(ClientModelValidationContext context)
        {
            Merge(context.Attributes, "data-val", "true");
            Merge(context.Attributes, "data-val-httpurl", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        }

        private static void Merge(IDictionary<string, string> attrs, string key, string value)
        {
            if (!attrs.ContainsKey(key))
            {
                attrs.Add(key, value);
            }
        }
    }
}
