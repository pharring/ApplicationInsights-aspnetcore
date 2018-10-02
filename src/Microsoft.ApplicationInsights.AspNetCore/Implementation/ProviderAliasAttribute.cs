namespace Microsoft.Extensions.Logging
{
    using System;

    /// <summary>
    /// Controls logger provider alias used for configuration
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal class ProviderAliasAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProviderAliasAttribute" /> class.
        /// </summary>
        public ProviderAliasAttribute(string alias)
        {
            Alias = alias;
        }

        /// <summary>
        /// Gets an alias that can be used insted full type name during configuration.
        /// </summary>
        public string Alias { get; }
    }
}