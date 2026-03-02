using System.Windows;
using MbCnsTool.Core.Models;
using MessageBox = System.Windows.MessageBox;

namespace MbCnsTool.Wpf;

/// <summary>
/// 自定义 OpenAI 兼容引擎配置窗口。
/// </summary>
public partial class CustomProviderWindow : Window
{
    /// <summary>
    /// 保存后的配置。
    /// </summary>
    public CustomOpenAiProviderOptions? ProviderOptions { get; private set; }

    /// <summary>
    /// 初始化窗口。
    /// </summary>
    public CustomProviderWindow(CustomOpenAiProviderOptions? currentOptions)
    {
        InitializeComponent();
        DisplayNameTextBox.Text = currentOptions?.DisplayName ?? "自定义OpenAI";
        BaseUrlTextBox.Text = currentOptions?.BaseUrl ?? "https://api.openai.com/v1";
        ModelTextBox.Text = currentOptions?.Model ?? "gpt-4.1-mini";
        ApiKeyPasswordBox.Password = currentOptions?.ApiKey ?? string.Empty;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var displayName = DisplayNameTextBox.Text.Trim();
        var baseUrl = BaseUrlTextBox.Text.Trim();
        var model = ModelTextBox.Text.Trim();
        var apiKey = ApiKeyPasswordBox.Password.Trim();

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(model) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(this, "Base URL、API Key、Model 不能为空。", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProviderOptions = new CustomOpenAiProviderOptions
        {
            ProviderKey = "custom_openai",
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "自定义OpenAI" : displayName,
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            Model = model
        };
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
