using Microsoft.AspNetCore.Components;

namespace OceanVMSClient.Components
{
    public partial class PageHeader
    {
        [Parameter]
        public string Title { get; set; } = "Page Title";

        [Parameter]
        public string? Subtitle { get; set; }

        /// <summary>
        /// Optional Material icon name, e.g. Icons.Material.Filled.Description
        /// </summary>
        [Parameter]
        public string? Icon { get; set; }

        /// <summary>
        /// Optional fragment rendered on the right side (actions, buttons, chips).
        /// </summary>
        [Parameter]
        public RenderFragment? RightContent { get; set; }

        // Allow callers to pass class/other attributes (e.g. Class="mb-0") to control spacing
        [Parameter(CaptureUnmatchedValues = true)]
        public Dictionary<string, object>? AdditionalAttributes { get; set; }

    }
}
