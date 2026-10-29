using System;
using Avalonia.Markup.Xaml;
using SecureGateway.Localization;

namespace SecureGateway.Mac
{
    /// <summary>XAML: Text="{loc:T 'Sync Shared'}" shows the text in the interface language.</summary>
    public class TExtension : MarkupExtension
    {
        public TExtension() { }
        public TExtension(string text) { Text = text; }
        public string Text { get; set; } = "";
        public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
    }
}
