using MDKOSS.Config.Wpf.Debug.Vision;
using MDKOSS.Core;
using MDKOSS.Core.Vision;
using OpenCvSharp;

namespace MDKOSS.Tests.Core.Vision;

public sealed class VisionDataflowExecutorTests
{
    [Fact]
    public void Version1_json_migrates_and_execute_without_loadImage_returns_pose()
    {
        var input = WriteBlobImage("v1-noload.png", 320, 240, 160, 120, 40);
        try
        {
            const string v1 = """
                {
                  "version": 1,
                  "algorithm": "opencv",
                  "nodes": [
                    { "id": "n-start", "kind": "start", "order": 0 },
                    { "id": "n-gray", "kind": "vision.toGray", "order": 1 },
                    { "id": "n-blob", "kind": "vision.findContours", "order": 2, "props": { "thresh": "80", "minArea": "50" } },
                    { "id": "n-out", "kind": "vision.outputPose", "order": 3, "props": { "prefix": "vision", "requireOk": "false" } },
                    { "id": "n-end", "kind": "end", "order": 4 }
                  ]
                }
                """;
            var doc = VisionDocument.Parse(v1);
            Assert.Equal(VisionVersions.Dataflow, doc.Version);
            Assert.True(doc.HasDataEdges());
            Assert.DoesNotContain(doc.Nodes, n =>
                string.Equals(n.Kind, VisionNodeKinds.LoadImage, StringComparison.OrdinalIgnoreCase));

            var catalogs = new Dictionary<string, VisionDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["inspect"] = doc,
            };
            var result = new VisionExecutor().Execute("inspect", VisionRunRequest.FromPath(input), id => catalogs.GetValueOrDefault(id));
            Assert.True(result.Ok, result.Error);
            Assert.True(result.Pose.Ok);
            Assert.InRange(result.Pose.X, 140, 180);
            Assert.InRange(result.Pose.Y, 100, 140);
            Assert.True(result.Vars.ContainsKey("vision.ok"));
        }
        finally
        {
            TryDelete(input);
        }
    }

    [Fact]
    public void Original_image_survives_threshold_for_templateMatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mdkoss-vision-orig-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var input = Path.Combine(dir, "scene.png");
        var template = Path.Combine(dir, "tpl.png");
        try
        {
            using (var mat = new Mat(240, 320, MatType.CV_8UC3, new Scalar(30, 30, 30)))
            {
                Cv2.Circle(mat, new Point(80, 120), 40, new Scalar(240, 240, 240), -1);
                DrawChecker(mat, 200, 90, 40, 8, new Scalar(40, 40, 140), new Scalar(90, 90, 200));
                Cv2.ImWrite(input, mat);
                using var crop = new Mat(mat, new Rect(200, 90, 40, 40)).Clone();
                Cv2.ImWrite(template, crop);
            }

            var start = "n-start";
            var th = "n-th";
            var match = "n-match";
            var output = "n-out";
            var end = "n-end";
            var doc = new VisionDocument
            {
                Version = VisionVersions.Dataflow,
                Nodes =
                [
                    new VisionNode { Id = start, Kind = VisionNodeKinds.Start, Order = 0 },
                    new VisionNode
                    {
                        Id = th,
                        Kind = VisionNodeKinds.Threshold,
                        Order = 1,
                        Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["mode"] = "binary",
                            ["thresh"] = "200",
                            ["maxVal"] = "255",
                        },
                    },
                    new VisionNode
                    {
                        Id = match,
                        Kind = VisionNodeKinds.TemplateMatch,
                        Order = 2,
                        Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["templatePath"] = template,
                            ["minScore"] = "0.7",
                        },
                    },
                    new VisionNode
                    {
                        Id = output,
                        Kind = VisionNodeKinds.OutputPose,
                        Order = 3,
                        Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["prefix"] = "vision",
                            ["requireOk"] = "false",
                        },
                    },
                    new VisionNode { Id = end, Kind = VisionNodeKinds.End, Order = 4 },
                ],
                Edges =
                [
                    new VisionEdge { From = start, To = th, Port = VisionPorts.Next },
                    new VisionEdge { From = th, To = match, Port = VisionPorts.Next },
                    new VisionEdge { From = match, To = output, Port = VisionPorts.Next },
                    new VisionEdge { From = output, To = end, Port = VisionPorts.Next },
                    VisionEdge.Data(start, VisionPorts.Image, th, VisionPorts.Image),
                    VisionEdge.Data(start, VisionPorts.Image, match, VisionPorts.Image),
                    VisionEdge.Data(match, VisionPorts.Pose, output, VisionPorts.Pose),
                ],
            };

            var result = new VisionExecutor().Run(doc, input);
            Assert.True(result.Ok, result.Error);
            Assert.True(result.Pose.Ok, result.Pose.Message);
            Assert.True(result.Pose.Score >= 0.7, $"score={result.Pose.Score}");
            Assert.InRange(result.Pose.X, 210, 230);
            Assert.InRange(result.Pose.Y, 100, 120);
        }
        finally
        {
            TryDelete(input);
            TryDelete(template);
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Branch_shares_source_image_to_contours_and_circles()
    {
        var input = WriteRingImage("branch.png", 320, 240, 160, 120, 48, 12);
        try
        {
            var start = "n-start";
            var blob = "n-blob";
            var circle = "n-circle";
            var outBlob = "n-out-blob";
            var outCircle = "n-out-circle";
            var end = "n-end";
            var doc = new VisionDocument
            {
                Version = VisionVersions.Dataflow,
                Nodes =
                [
                    new VisionNode { Id = start, Kind = VisionNodeKinds.Start, Order = 0 },
                    new VisionNode
                    {
                        Id = blob,
                        Kind = VisionNodeKinds.FindContours,
                        Order = 1,
                        Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["thresh"] = "80",
                            ["minArea"] = "50",
                        },
                    },
                    new VisionNode
                    {
                        Id = circle,
                        Kind = VisionNodeKinds.FindCircles,
                        Order = 2,
                        Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["minDist"] = "20",
                            ["minRadius"] = "20",
                            ["maxRadius"] = "80",
                            ["param2"] = "20",
                        },
                    },
                    new VisionNode
                    {
                        Id = outBlob,
                        Kind = VisionNodeKinds.OutputPose,
                        Order = 3,
                        Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["prefix"] = "blob",
                            ["requireOk"] = "false",
                        },
                    },
                    new VisionNode
                    {
                        Id = outCircle,
                        Kind = VisionNodeKinds.OutputPose,
                        Order = 4,
                        Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["prefix"] = "circle",
                            ["requireOk"] = "false",
                        },
                    },
                    new VisionNode { Id = end, Kind = VisionNodeKinds.End, Order = 5 },
                ],
                Edges =
                [
                    new VisionEdge { From = start, To = blob, Port = VisionPorts.Next },
                    new VisionEdge { From = blob, To = circle, Port = VisionPorts.Next },
                    new VisionEdge { From = circle, To = outBlob, Port = VisionPorts.Next },
                    new VisionEdge { From = outBlob, To = outCircle, Port = VisionPorts.Next },
                    new VisionEdge { From = outCircle, To = end, Port = VisionPorts.Next },
                    VisionEdge.Data(start, VisionPorts.Image, blob, VisionPorts.Image),
                    VisionEdge.Data(start, VisionPorts.Image, circle, VisionPorts.Image),
                    VisionEdge.Data(blob, VisionPorts.Pose, outBlob, VisionPorts.Pose),
                    VisionEdge.Data(circle, VisionPorts.Pose, outCircle, VisionPorts.Pose),
                ],
            };

            var result = new VisionExecutor().Run(doc, input);
            Assert.True(result.Ok, result.Error);
            Assert.Equal(true, result.Vars["blob.ok"]);
            Assert.Equal(true, result.Vars["circle.ok"]);
            Assert.InRange(Convert.ToDouble(result.Vars["blob.x"]), 140, 180);
            Assert.InRange(Convert.ToDouble(result.Vars["circle.x"]), 140, 180);
        }
        finally
        {
            TryDelete(input);
        }
    }

    [Fact]
    public void Trial_run_exposes_per_node_input_output_images_and_vars()
    {
        var input = WriteBlobImage("trial.png", 320, 240, 160, 120, 40);
        var traceDir = Path.Combine(Path.GetTempPath(), "mdkoss-vision-trial-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var vm = new VisionEditorVm();
            vm.Load(VisionDocument.CreateBasicInspectPipeline());
            var doc = vm.ToDocument();
            Assert.Equal(VisionVersions.Dataflow, doc.Version);
            Assert.True(doc.HasDataEdges());

            var result = new VisionExecutor().Run(doc, new VisionRunRequest
            {
                InputImagePath = input,
                KeepIntermediates = true,
                TraceDirectory = traceDir,
            });
            Assert.True(result.Ok, result.Error);
            Assert.NotEmpty(result.NodeTraces);

            vm.ApplyRunResult(result);
            var th = vm.Nodes.First(n => string.Equals(n.Kind, VisionNodeKinds.Threshold, StringComparison.OrdinalIgnoreCase));
            vm.Selected = th;
            var trace = vm.SelectedTrace;
            Assert.NotNull(trace);
            Assert.True(trace!.InputWidth > 0);
            Assert.True(trace.OutputWidth > 0);
            Assert.True(File.Exists(trace.InputImagePath));
            Assert.True(File.Exists(trace.OutputImagePath));
            Assert.Contains("输入", vm.SelectedTraceSummary);

            var blob = vm.Nodes.First(n => string.Equals(n.Kind, VisionNodeKinds.FindContours, StringComparison.OrdinalIgnoreCase));
            vm.Selected = blob;
            Assert.NotEmpty(vm.SelectedTrace!.OutputVars);
        }
        finally
        {
            TryDelete(input);
            TryDeleteDir(traceDir);
        }
    }

    [Fact]
    public void Production_mode_100_runs_releases_intermediates()
    {
        var input = WriteBlobImage("mem.png", 160, 120, 80, 60, 24);
        try
        {
            var doc = VisionDocument.CreateBasicInspectPipeline();
            var executor = new VisionExecutor();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetTotalMemory(true);

            VisionRunResult? last = null;
            for (var i = 0; i < 100; i++)
            {
                last = executor.Run(doc, new VisionRunRequest { InputImagePath = input });
                Assert.True(last.Ok, last.Error);
                Assert.Empty(last.NodeTraces);
            }

            Assert.NotNull(last);
            Assert.True(last!.Pose.Ok);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var after = GC.GetTotalMemory(true);
            Assert.True(after - before < 32 * 1024 * 1024, $"memory grew by {after - before} bytes");
        }
        finally
        {
            TryDelete(input);
        }
    }

    [Fact]
    public void Execute_accepts_in_memory_bytes_and_visiondev_keeps_var_names()
    {
        var input = WriteBlobImage("bytes.png", 320, 240, 160, 120, 40);
        try
        {
            var bytes = File.ReadAllBytes(input);
            var doc = CreateNoLoadInspect();
            var result = new VisionExecutor().Run(doc, VisionRunRequest.FromBytes(bytes));
            Assert.True(result.Ok, result.Error);
            Assert.True(result.Pose.Ok);

            var visions = new Dictionary<string, MdkSetting.VisionConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["inspect"] = new MdkSetting.VisionConfig
                {
                    Id = "inspect",
                    Name = "inspect",
                    Pipeline = doc,
                },
            };
            var vars = new MVarStore();
            var device = new VisionDevice(
                "vd-1",
                "vision",
                new VisionDeviceParameters
                {
                    VisionId = "inspect",
                    ResultPrefix = "vision",
                    GenerateTestImageWhenMissing = false,
                },
                vars,
                id => visions.GetValueOrDefault(id),
                _ => null);

            var run = device.Execute(null, VisionRunRequest.FromBytes(bytes));
            Assert.True(run.Ok, run.Error);
            Assert.True(vars.TryGet<object>("vision.ok", out var ok) && ok is true);
            Assert.True(vars.TryGet<object>("vision.x", out _));
            Assert.True(vars.TryGet<object>("vision.y", out _));
            Assert.True(vars.TryGet<object>("vision.angle", out _));
            Assert.True(vars.TryGet<object>("vision.score", out _));
        }
        finally
        {
            TryDelete(input);
        }
    }

    private static VisionDocument CreateNoLoadInspect()
    {
        var nodes = new List<VisionNode>
        {
            new() { Id = "n-start", Kind = VisionNodeKinds.Start, Order = 0 },
            new() { Id = "n-gray", Kind = VisionNodeKinds.ToGray, Order = 1 },
            new()
            {
                Id = "n-blob",
                Kind = VisionNodeKinds.FindContours,
                Order = 2,
                Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["thresh"] = "80",
                    ["minArea"] = "50",
                },
            },
            new()
            {
                Id = "n-out",
                Kind = VisionNodeKinds.OutputPose,
                Order = 3,
                Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["prefix"] = "vision",
                    ["requireOk"] = "false",
                },
            },
            new() { Id = "n-end", Kind = VisionNodeKinds.End, Order = 5 },
        };
        var doc = new VisionDocument { Version = VisionVersions.Dataflow, Nodes = nodes };
        doc.RebuildLinearEdges();
        return doc;
    }

    private static void DrawChecker(Mat mat, int x, int y, int size, int cell, Scalar a, Scalar b)
    {
        for (var row = 0; row < size; row += cell)
        {
            for (var col = 0; col < size; col += cell)
            {
                var color = ((row / cell) + (col / cell)) % 2 == 0 ? a : b;
                Cv2.Rectangle(mat, new Rect(x + col, y + row, cell, cell), color, -1);
            }
        }
    }

    private static string WriteBlobImage(string name, int w, int h, int cx, int cy, int radius)
    {
        var path = Path.Combine(Path.GetTempPath(), "mdkoss-vision-" + name);
        using var mat = new Mat(h, w, MatType.CV_8UC3, new Scalar(10, 10, 10));
        Cv2.Circle(mat, new Point(cx, cy), radius, new Scalar(240, 240, 240), -1);
        Cv2.ImWrite(path, mat);
        return path;
    }

    private static string WriteRingImage(string name, int w, int h, int cx, int cy, int outer, int inner)
    {
        var path = Path.Combine(Path.GetTempPath(), "mdkoss-vision-" + name);
        using var mat = new Mat(h, w, MatType.CV_8UC3, new Scalar(20, 20, 20));
        Cv2.Circle(mat, new Point(cx, cy), outer, new Scalar(240, 240, 240), -1);
        Cv2.Circle(mat, new Point(cx, cy), inner, new Scalar(40, 40, 40), -1);
        Cv2.ImWrite(path, mat);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore temp cleanup
        }
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore temp cleanup
        }
    }
}
