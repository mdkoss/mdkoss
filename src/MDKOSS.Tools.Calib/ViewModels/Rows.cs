namespace MDKOSS.Tools.Calib.ViewModels;

public sealed class ParamRow : ObservableObject
{
    private string _key = "";
    private string _value = "";

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

public sealed class ResultRow : ObservableObject
{
    private string _key = "";
    private string _value = "";

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

public sealed class LogLine
{
    public LogLine(DateTime time, string level, string text)
    {
        Time = time;
        Level = level;
        Text = text;
    }

    public DateTime Time { get; }

    public string Level { get; }

    public string Text { get; }

    public string Display => $"{Time:HH:mm:ss.fff}  [{Level}]  {Text}";
}

public sealed class FlowNodeRow : ObservableObject
{
    private string _id = "";
    private string _kind = "op.log";
    private string _propsText = "";

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Kind
    {
        get => _kind;
        set => SetProperty(ref _kind, value);
    }

    public string PropsText
    {
        get => _propsText;
        set => SetProperty(ref _propsText, value);
    }
}
