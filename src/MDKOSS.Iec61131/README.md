# MDKOSS.Iec61131

把 MDKOSS **Flow 任务 / 变量 / GPIO** 导出为 IEC 61131-3 工程（SCL + PLCopen XML），供 TIA Portal / CODESYS 导入。

不编译任意 C# `Task`。`type=flow` 的节点图变成 FB 步序；运动与 `deviceAction` 变成 Execute→Done 握手块。

```csharp
var setting = MdkSetting.Load("station.setting.json");
var project = IecProjectBuilder.FromSetting(setting, Path.GetDirectoryName(path));
IecExport.Write(project, "export");
```

实例与转换结果见 `MDKOSS.Sample.Iec61131`。
