using System;
using System.Windows.Markup;
using SecureGateway.Localization;

namespace SecureGateway.UI
{
    /// <summary>XAML: Text="{loc:T 'Sync Shared'}" shows the text in the interface language.</summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class TExtension : MarkupExtension
    {
        public TExtension() { }
        public TExtension(string text) { Text = text; }
        public string Text { get; set; } = "";
        public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
    }
}
