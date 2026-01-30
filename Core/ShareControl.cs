using DiffMatchPatch;
using MajdataEdit.ChartShare;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MajdataEdit;

public partial class MainWindow : Window
{
    private async Task ToggleChartShare(bool initIfClose = false)
    {
        Stop();
        if (ChartServer.App != null)
        {
            if (!isSaved) if (!AskSaveFumen()) return;

            if (_client != null)
            {
                await _client!.StopAsync();
                _client = null;
            }
            await ChartServer.StopAsync();

            if (initIfClose) await InitFromFile(originMaidataDir);

            set_host(false);
            return;
        }

        var text = GetRawFumenText();
        if (text == "")
        {
            MessageBox.Show(GetLocalizedString("ShareEmpty"), GetLocalizedString("Error"));
            return;
        }
        _shadowText = text;
        var cds = new HubDataService(
            SimaiProcess.simaiFile.Title,
            selectedDifficulty,
            SimaiProcess.levels[selectedDifficulty],
            text,
            SimaiProcess.simaiFile.Offset,
            useOgg);
        await ChartServer.StartAsync(cds, maidataDir);
        set_host(true);
        await ConnectToChartServer("127.0.0.1", 8014);
    }
    // 返回是否连接成功
    private async Task<bool> ConnectToChartServer(string ip, int port)
    {
        Stop();
        try
        {
            if (_client != null)
            {
                await _client.StartAsync();
                return false;
            }

            string hubUrl = $"https://{ip}:{port}/chartHub";
            string fileUrl = $"https://{ip}:{port}/chartFiles";

            _client = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.HttpMessageHandlerFactory = (handler) =>
                    {
                        if (handler is HttpClientHandler clientHandler)
                        {
                            clientHandler.ServerCertificateCustomValidationCallback =
                                (message, cert, chain, errors) =>
                                {
                                    if (cert == null) return false;
                                    return cert.GetPublicKey().SequenceEqual(certBytes);
                                };
                        }
                        return handler;
                    };

                    options.WebSocketConfiguration = sockets =>
                    {
                        sockets.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                        {
                            if (cert == null) return false;
                            return cert.GetPublicKey().SequenceEqual(certBytes);
                        };
                    };
                })
                .Build();


            // 收到初始化数据 (文本、音频)
            _client.On<GuestInitDto>(nameof(IEditorClient.OnJoined), (data) =>
            {
                Dispatcher.Invoke(async () =>
                {
                    if (!isSaved) if (!AskSaveFumen(false)) return;
                    await InitFromShare(fileUrl, data);
                    set_share(true);
                });
            });

            // 有人加入了
            _client.On<ClientConnectDto>(nameof(IEditorClient.OnUserJoined), async (user) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    ShowStatusMessage(string.Format(GetLocalizedString("UserJoined"), user.UserName));
                });
            });

            // 有人离开了
            _client.On<ClientConnectDto, string>(nameof(IEditorClient.OnUserLeft), async (user, message) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    ShowStatusMessage(string.Format(GetLocalizedString("UserLeft"), user.UserName, message));
                });
            });

            // 收到远程用户的编辑操作
            _client.On<string>(nameof(IEditorClient.OnTyping), async (patchText) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _isRemoteUpdate = true; //防止死循环

                    string currentUiText = GetRawFumenText();
                    var patches = _dmp.patch_fromText(patchText);
                    var result = _dmp.patch_apply(patches, currentUiText);
                    string newText = (string)result[0];

                    var cursor = GetRawFumenPosition();

                    foreach (var patch in patches)
                    {
                        var patchStart = patch.start1;
                        var patchEnd = patchStart + patch.length1;
                        var lengthDiff = patch.length2 - patch.length1;

                        // 补丁块完全在光标之后
                        //（由于EQUAL diff，几个字符内会误判到覆盖光标，但是这样至少能快点）
                        if (patchStart > cursor)
                        {
                            continue;
                        }

                        // 补丁块完全在光标之前
                        if (patchEnd <= cursor)
                        {
                            cursor += lengthDiff;
                            continue; // 下一个补丁
                        }

                        // 补丁块覆盖了光标
                        var currentPos = patchStart;
                        foreach (var diff in patch.diffs)
                        {
                            int len = diff.text.Length;
                            if (diff.operation == Operation.EQUAL)
                            {
                                currentPos += len;
                            }
                            else if (diff.operation == Operation.DELETE)
                            {
                                // 如果删除内容在光标后面就不用管（前文误判因素）
                                if (currentPos > cursor) break;

                                // 如果光标在被删除的区间内，回退到删除点起点
                                if (currentPos + len > cursor) cursor = currentPos;
                                else if (currentPos + len <= cursor) cursor -= len;
                                currentPos += len;
                            }
                            else if (diff.operation == Operation.INSERT)
                            {
                                // 如果插入点在光标前，光标后移
                                if (currentPos <= cursor) cursor += len;
                            }
                        }
                    }

                    SetRawFumenText(newText); // 这里的光标变化会被拦截不同步
                    _shadowText = newText;

                    FumenContent.Focus();

                    _isRemoteUpdate = false;

                    SetRawFumenPosition(cursor); // 这里的光标处理需要同步
                });
            });

            //光标移动信息
            _client.On<Dictionary<string, RemoteCursor>>(nameof(IEditorClient.OnSyncCursors), async (cursors) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _cursors = cursors.Where(c => c.Key != _client.ConnectionId).ToDictionary(kv => kv.Key, kv => kv.Value);
                    RenderAllCursors();
                });
            });

            //接收到保存信息
            _client.On<string>(nameof(IEditorClient.OnSaveFumen), async (text) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (text == "") SetSavedState(true); //为空表示只是状态更新
                    else SaveFumen(true);
                });
            });

            //客户端关闭
            _client.Closed += async (exception) =>
            {
                await Dispatcher.Invoke(async () =>
                {
                    if (exception != null)
                    {
                        MessageBox.Show(string.Format(GetLocalizedString("ConnectionClosed"), exception.Message + exception.InnerException?.Message), GetLocalizedString("Error"));
                    }

                    _client = null;
                    await DisconnectToChartServer();
                });
            };

            await _client.StartAsync();
            await _client.SendAsync(nameof(ChartHub.GuestInit), new ClientConnectDto()
            {
                UserName = editorSetting!.ShareUserName,
                ColorHex = editorSetting!.ShareColorHex,
                isHost = IsHost
            });
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(string.Format(GetLocalizedString("ConnectFail"), exception.Message + exception.InnerException?.Message), GetLocalizedString("Error"));
            _client = null;
            set_share(false);
            return false;
        }
    }

    private async Task DisconnectToChartServer()
    {
        if (_client != null)
        {
            if (_client.State == HubConnectionState.Connected)
            {
                SaveFumen(true);
                await _client.StopAsync();
            }
            _client = null;
        }
        _cursors.Clear();

        set_share(false);
        ClearWindow(true);
    }

    private async Task SyncChartServer()
    {
        if (_isRemoteUpdate || !IsShare) return;
        if (_client!.State != HubConnectionState.Connected) return;

        string currentUiText = GetRawFumenText();

        var diffs = _dmp.diff_main(_shadowText, currentUiText);
        if (diffs.Count == 0) return;
        var patches = _dmp.patch_make(_shadowText, diffs);
        var patchText = _dmp.patch_toText(patches);

        _shadowText = currentUiText;

        await _client.InvokeAsync(nameof(ChartHub.Typing), patchText);
    }

    private async void ShowStatusMessage(string message, int durationMs = 3000)
    {
        ShareStatus.Text = message;
        await Task.Delay(durationMs);
        if (ShareStatus.Text == message)
        {
            ShareStatus.Text = string.Format(GetLocalizedString("ShareModeServer"), GetLocalIPAddress());
        }
    }

    private void RenderAllCursors()
    {
        FumenContent.UpdateLayout();
        CursorOverlay.Children.Clear();

        foreach (var kvp in _cursors)
        {
            var cursor = kvp.Value;

            Rect rect = FumenContent.GetRectFromCharacterIndex(ToUiIndex(cursor.Index));
            Point screenPos = FumenContent.TranslatePoint(rect.TopLeft, CursorOverlay);
            var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(cursor.ColorHex));

            Border cursorLine = new()
            {
                Width = 1,
                // 如果是空行，rect.Height 可能会很小，给个保底值 18
                Height = rect.Height > 18 ? rect.Height : 18,
                Background = brush,
                IsHitTestVisible = false
            };

            // 名字标签
            var nameTag = new TextBlock
            {
                Text = cursor.UserName,
                FontSize = 10,
                Foreground = System.Windows.Media.Brushes.WhiteSmoke,
                Padding = new Thickness(2, 0, 2, 0),
                IsHitTestVisible = false
            };

            Canvas.SetLeft(cursorLine, screenPos.X);
            Canvas.SetTop(cursorLine, screenPos.Y);
            Canvas.SetLeft(nameTag, screenPos.X + 0.5);
            Canvas.SetTop(nameTag, screenPos.Y - 7);

            CursorOverlay.Children.Add(cursorLine);
            CursorOverlay.Children.Add(nameTag);
        }
    }
}
