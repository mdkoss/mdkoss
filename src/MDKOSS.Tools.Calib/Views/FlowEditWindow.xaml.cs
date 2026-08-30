using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using MDKOSS.Core.Flow;
using MDKOSS.Tools.Calib.ViewModels;

namespace MDKOSS.Tools.Calib.Views;

public partial class FlowEditWindow : Window
{
    private static readonly JsonSerializerOptions PropsJson = new()
    {
        WriteIndented = false,
    };

    public FlowEditWindow(FlowDocument document)
    {
        InitializeComponent();
        Nodes = [];
        KindChoices = FlowNodeKinds.All;
        Load(document);
        DataContext = this;
        NodeList.ItemsSource = Nodes;
    }

    public ObservableCollection<FlowNodeRow> Nodes { get; }

    public IReadOnlyList<string> KindChoices { get; }

    public FlowDocument? Result { get; private set; }

    public FlowNodeRow? SelectedNode
    {
        get => NodeList.SelectedItem as FlowNodeRow;
        set => NodeList.SelectedItem = value;
    }

    private void Load(FlowDocument document)
    {
        Nodes.Clear();
        foreach (var node in document.Nodes.OrderBy(n => n.Order).ThenBy(n => n.Y))
        {
            Nodes.Add(new FlowNodeRow
            {
                Id = node.Id,
                Kind = node.Kind,
                PropsText = SerializeProps(node.Props),
            });
        }

        JsonBox.Text = document.ToJson();
        ValidationText.Text = FormatValidation(document.Validate());
    }

    private void AddNode_Click(object sender, RoutedEventArgs e)
    {
        var id = "n-" + (Nodes.Count + 1);
        Nodes.Add(new FlowNodeRow { Id = id, Kind = FlowNodeKinds.OpLog, PropsText = "{\"message\":\"\\\"step\\\"\"}" });
        NodeList.SelectedIndex = Nodes.Count - 1;
    }

    private void DeleteNode_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNode is null)
        {
            return;
        }

        Nodes.Remove(SelectedNode);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var index = NodeList.SelectedIndex;
        if (index <= 0)
        {
            return;
        }

        Nodes.Move(index, index - 1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var index = NodeList.SelectedIndex;
        if (index < 0 || index >= Nodes.Count - 1)
        {
            return;
        }

        Nodes.Move(index, index + 1);
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuild(out var doc, out var error))
        {
            ValidationText.Text = error;
            return;
        }

        JsonBox.Text = doc.ToJson();
        ValidationText.Text = FormatValidation(doc.Validate());
    }

    private void FromJson_Click(object sender, RoutedEventArgs e)
    {
        if (!FlowDocument.TryParse(JsonBox.Text, out var doc, out var error))
        {
            ValidationText.Text = error ?? "JSON 无效";
            return;
        }

        Load(doc);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Tab.SelectedIndex == 1)
        {
            if (!FlowDocument.TryParse(JsonBox.Text, out var fromJson, out var parseError))
            {
                MessageBox.Show(parseError ?? "JSON 无效", "流程", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var jsonErrors = fromJson.Validate();
            if (jsonErrors.Count > 0)
            {
                MessageBox.Show(string.Join("\n", jsonErrors), "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = fromJson;
            DialogResult = true;
            return;
        }

        if (!TryBuild(out var doc, out var error))
        {
            MessageBox.Show(error, "流程", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var errors = doc.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = doc;
        DialogResult = true;
    }

    private bool TryBuild(out FlowDocument document, out string error)
    {
        document = new FlowDocument { Version = 1 };
        error = "";
        var nodes = new List<FlowNode>();
        for (var i = 0; i < Nodes.Count; i++)
        {
            var row = Nodes[i];
            if (string.IsNullOrWhiteSpace(row.Id))
            {
                error = $"第 {i + 1} 个节点缺少 Id";
                return false;
            }

            if (!TryParseProps(row.PropsText, out var props, out var propsError))
            {
                error = $"节点 {row.Id}: {propsError}";
                return false;
            }

            nodes.Add(new FlowNode
            {
                Id = row.Id.Trim(),
                Kind = string.IsNullOrWhiteSpace(row.Kind) ? FlowNodeKinds.OpLog : row.Kind.Trim(),
                X = 300,
                Y = 40 + i * 80,
                Order = i,
                Props = props,
            });
        }

        if (nodes.All(n => !string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase)))
        {
            nodes.Insert(0, new FlowNode { Id = "n-start", Kind = FlowNodeKinds.Start, X = 300, Y = 40, Order = 0 });
        }

        if (nodes.All(n => !string.Equals(n.Kind, FlowNodeKinds.End, StringComparison.OrdinalIgnoreCase)))
        {
            nodes.Add(new FlowNode { Id = "n-end", Kind = FlowNodeKinds.End, X = 300, Y = 40 + nodes.Count * 80, Order = nodes.Count });
        }

        document.Nodes = nodes;
        document.Functions = [new FlowFunction { Name = "main", EntryNodeId = nodes[0].Id }];
        document.Edges = [];
        for (var i = 0; i < nodes.Count - 1; i++)
        {
            var kind = (nodes[i].Kind ?? "").ToLowerInvariant();
            if (kind is "if" or "while")
            {
                error = "线性编辑器不支持 if/while，请改用 JSON 页签保留端口。";
                return false;
            }

            document.Edges.Add(new FlowEdge { From = nodes[i].Id, To = nodes[i + 1].Id, Port = FlowPorts.Next });
        }

        return true;
    }

    private static bool TryParseProps(string? raw, out Dictionary<string, string> props, out string error)
    {
        props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "props 必须是 JSON 对象";
                return false;
            }

            foreach (var p in doc.RootElement.EnumerateObject())
            {
                props[p.Name] = p.Value.ValueKind == JsonValueKind.String
                    ? p.Value.GetString() ?? ""
                    : p.Value.ToString();
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string SerializeProps(Dictionary<string, string>? props)
    {
        if (props is null || props.Count == 0)
        {
            return "{}";
        }

        return JsonSerializer.Serialize(props, PropsJson);
    }

    private static string FormatValidation(IReadOnlyList<string> errors) =>
        errors.Count == 0 ? "校验通过" : string.Join(Environment.NewLine, errors);
}
