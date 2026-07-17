using System.IO;
using System.Runtime.InteropServices;
using UDS_Print_Master.Models;

namespace UDS_Print_Master.Services;

public sealed class WiaScannerService
{
    private const int ScannerDeviceType = 1;
    private const int DocumentHandlingCapabilitiesProperty = 3086;
    private const int DocumentHandlingStatusProperty = 3087;
    private const int DocumentHandlingSelectProperty = 3088;
    private const int CurrentIntentProperty = 6146;
    private const int HorizontalResolutionProperty = 6147;
    private const int VerticalResolutionProperty = 6148;
    private const int HorizontalStartPositionProperty = 6149;
    private const int VerticalStartPositionProperty = 6150;
    private const int HorizontalExtentProperty = 6151;
    private const int VerticalExtentProperty = 6152;

    private const int FeederFlag = 1;
    private const int FlatbedFlag = 2;
    private const int DuplexFlag = 4;
    private const int ColorIntent = 1;
    private const int GrayscaleIntent = 2;
    private const int TextIntent = 4;

    private const uint WiaErrorPaperJam = 0x80210002;
    private const uint WiaErrorPaperEmpty = 0x80210003;
    private const uint WiaErrorOffline = 0x80210005;
    private const uint WiaErrorBusy = 0x80210006;
    private const uint WiaErrorCoverOpen = 0x80210016;

    private const string PngFormat = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";

    public Task<IReadOnlyList<ScannerDeviceInfo>> GetScannersAsync()
    {
        return RunStaAsync<IReadOnlyList<ScannerDeviceInfo>>(GetScanners);
    }

    public Task<ScannerCapabilities> GetCapabilitiesAsync(string deviceId)
    {
        return RunStaAsync(() => GetCapabilities(deviceId));
    }

    public Task ScanAsync(ScanRequest request, IProgress<string> pageProgress, CancellationToken cancellationToken)
    {
        return RunStaAsync(() =>
        {
            Scan(request, pageProgress, cancellationToken);
            return true;
        });
    }

    private static IReadOnlyList<ScannerDeviceInfo> GetScanners()
    {
        var result = new List<ScannerDeviceInfo>();
        object? managerObject = null;

        try
        {
            var managerType = Type.GetTypeFromProgID("WIA.DeviceManager")
                              ?? throw new InvalidOperationException("Компонент сканирования Windows (WIA) недоступен.");
            managerObject = Activator.CreateInstance(managerType)
                            ?? throw new InvalidOperationException("Не удалось запустить компонент сканирования Windows.");
            dynamic manager = managerObject;
            dynamic infos = manager.DeviceInfos;
            var count = (int)infos.Count;

            for (var index = 1; index <= count; index++)
            {
                object? infoObject = null;
                try
                {
                    infoObject = infos[index];
                    dynamic info = infoObject;
                    if ((int)info.Type != ScannerDeviceType)
                    {
                        continue;
                    }

                    var deviceId = Convert.ToString(info.DeviceID) ?? string.Empty;
                    var name = ReadStringProperty(info.Properties, 7);
                    if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "No friendly name", StringComparison.OrdinalIgnoreCase))
                    {
                        name = ReadStringProperty(info.Properties, 4)
                               ?? ReadStringProperty(info.Properties, 3)
                               ?? "WIA-сканер";
                    }
                    result.Add(new ScannerDeviceInfo(deviceId, name));
                }
                finally
                {
                    ReleaseComObject(infoObject);
                }
            }
        }
        finally
        {
            ReleaseComObject(managerObject);
        }

        return result;
    }

    private static ScannerCapabilities GetCapabilities(string deviceId)
    {
        object? deviceObject = null;
        try
        {
            deviceObject = Connect(deviceId);
            dynamic device = deviceObject;
            dynamic properties = device.Properties;

            var reportedCapabilities = ReadIntProperty(properties, DocumentHandlingCapabilitiesProperty) ?? 0;
            var supportedHandling = ReadSupportedFlags(properties, DocumentHandlingSelectProperty);
            var status = ReadIntProperty(properties, DocumentHandlingStatusProperty) ?? 0;
            var selectionProperty = FindProperty(properties, DocumentHandlingSelectProperty);
            var hasSelectionProperty = selectionProperty is not null;
            ReleaseComObject(selectionProperty);

            var installedHandling = reportedCapabilities != 0 ? reportedCapabilities : supportedHandling;

            var hasFlatbed = !hasSelectionProperty || (installedHandling & FlatbedFlag) != 0 || (status & FlatbedFlag) != 0;
            var hasFeeder = (installedHandling & FeederFlag) != 0 || (status & FeederFlag) != 0;
            var supportsDuplex = (installedHandling & DuplexFlag) != 0 || (status & DuplexFlag) != 0;

            dynamic item = device.Items[1];
            var supportedDpi = ReadSupportedDpi(item.Properties);
            ReleaseComObject(item);

            return new ScannerCapabilities(hasFlatbed, hasFeeder, supportsDuplex, supportedDpi);
        }
        finally
        {
            ReleaseComObject(deviceObject);
        }
    }

    private static void Scan(ScanRequest request, IProgress<string> pageProgress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(request.OutputFolder);
        object? deviceObject = null;

        try
        {
            deviceObject = Connect(request.DeviceId);
            dynamic device = deviceObject;

            var sourceFlags = request.UseFeeder ? FeederFlag : FlatbedFlag;
            if (request.Duplex)
            {
                sourceFlags |= DuplexFlag;
            }

            SetProperty(device.Properties, DocumentHandlingSelectProperty, sourceFlags, required: request.UseFeeder || request.Duplex);

            var scannedPages = 0;
            var maximumPages = request.UseFeeder ? 500 : 1;
            while (scannedPages < maximumPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? itemObject = null;
                object? imageObject = null;

                try
                {
                    itemObject = device.Items[1];
                    dynamic item = itemObject;
                    ConfigureItem(item, request);

                    imageObject = item.Transfer(PngFormat);
                    dynamic image = imageObject;
                    var outputPath = Path.Combine(request.OutputFolder, $"scan-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.png");
                    image.SaveFile(outputPath);
                    scannedPages++;
                    pageProgress.Report(outputPath);

                    if (!request.UseFeeder)
                    {
                        break;
                    }
                }
                catch (COMException ex) when (request.UseFeeder && scannedPages > 0 && unchecked((uint)ex.HResult) == WiaErrorPaperEmpty)
                {
                    break;
                }
                catch (COMException ex)
                {
                    throw CreateFriendlyScanException(ex);
                }
                finally
                {
                    ReleaseComObject(imageObject);
                    ReleaseComObject(itemObject);
                }
            }

            if (scannedPages == 0)
            {
                throw new InvalidOperationException(request.UseFeeder
                    ? "В автоподатчике не обнаружены листы."
                    : "Сканер не вернул изображение.");
            }
        }
        finally
        {
            ReleaseComObject(deviceObject);
        }
    }

    private static void ConfigureItem(dynamic item, ScanRequest request)
    {
        var intent = request.ColorMode switch
        {
            "Grayscale" => GrayscaleIntent,
            "BlackAndWhite" => TextIntent,
            _ => ColorIntent
        };

        SetProperty(item.Properties, CurrentIntentProperty, intent);
        SetProperty(item.Properties, HorizontalResolutionProperty, request.Dpi, required: true);
        SetProperty(item.Properties, VerticalResolutionProperty, request.Dpi, required: true);
        SetProperty(item.Properties, HorizontalStartPositionProperty, 0);
        SetProperty(item.Properties, VerticalStartPositionProperty, 0);
        SetPropertyToMaximum(item.Properties, HorizontalExtentProperty);
        SetPropertyToMaximum(item.Properties, VerticalExtentProperty);
    }

    private static object Connect(string deviceId)
    {
        object? managerObject = null;
        try
        {
            var managerType = Type.GetTypeFromProgID("WIA.DeviceManager")
                              ?? throw new InvalidOperationException("Компонент сканирования Windows (WIA) недоступен.");
            managerObject = Activator.CreateInstance(managerType)
                            ?? throw new InvalidOperationException("Не удалось запустить компонент сканирования Windows.");
            dynamic manager = managerObject;
            dynamic infos = manager.DeviceInfos;
            var count = (int)infos.Count;

            for (var index = 1; index <= count; index++)
            {
                object? infoObject = null;
                try
                {
                    infoObject = infos[index];
                    dynamic info = infoObject;
                    if (string.Equals(Convert.ToString(info.DeviceID), deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        return info.Connect();
                    }
                }
                finally
                {
                    ReleaseComObject(infoObject);
                }
            }

            throw new InvalidOperationException("Выбранный сканер больше не доступен.");
        }
        finally
        {
            ReleaseComObject(managerObject);
        }
    }

    private static IReadOnlyList<int> ReadSupportedDpi(dynamic properties)
    {
        object? propertyObject = FindProperty(properties, HorizontalResolutionProperty);
        if (propertyObject is null)
        {
            return [150, 300, 600];
        }

        try
        {
            var values = ReadSubtypeValues(propertyObject)
                .Where(value => value is >= 75 and <= 2400)
                .Distinct()
                .OrderBy(value => value)
                .ToList();

            if (values.Count == 0)
            {
                var minimum = ReadSubtypeBound(propertyObject, maximum: false) ?? 75;
                var maximum = ReadSubtypeBound(propertyObject, maximum: true) ?? 1200;
                values.AddRange(new[] { 150, 200, 300, 400, 600, 1200 }.Where(value => value >= minimum && value <= maximum));
            }

            return values.Count > 0 ? values : [300];
        }
        finally
        {
            ReleaseComObject(propertyObject);
        }
    }

    private static int ReadSupportedFlags(dynamic properties, int propertyId)
    {
        object? propertyObject = FindProperty(properties, propertyId);
        if (propertyObject is null)
        {
            return 0;
        }

        try
        {
            var flags = 0;
            foreach (var value in ReadSubtypeValues(propertyObject))
            {
                flags |= value;
            }

            return flags;
        }
        finally
        {
            ReleaseComObject(propertyObject);
        }
    }

    private static List<int> ReadSubtypeValues(object propertyObject)
    {
        var result = new List<int>();
        object? valuesObject = null;
        try
        {
            dynamic property = propertyObject;
            valuesObject = property.SubTypeValues;
            dynamic values = valuesObject;
            var count = (int)values.Count;
            for (var index = 1; index <= count; index++)
            {
                result.Add(Convert.ToInt32(values[index]));
            }
        }
        catch
        {
            // Some drivers expose only a range and do not provide a values collection.
        }
        finally
        {
            ReleaseComObject(valuesObject);
        }

        return result;
    }

    private static string? ReadStringProperty(dynamic properties, int propertyId)
    {
        object? propertyObject = FindProperty(properties, propertyId);
        if (propertyObject is null)
        {
            return null;
        }

        try
        {
            dynamic property = propertyObject;
            return Convert.ToString(property.Value);
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseComObject(propertyObject);
        }
    }

    private static int? ReadIntProperty(dynamic properties, int propertyId)
    {
        object? propertyObject = FindProperty(properties, propertyId);
        if (propertyObject is null)
        {
            return null;
        }

        try
        {
            dynamic property = propertyObject;
            return Convert.ToInt32(property.Value);
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseComObject(propertyObject);
        }
    }

    private static object? FindProperty(dynamic properties, int propertyId)
    {
        var count = (int)properties.Count;
        for (var index = 1; index <= count; index++)
        {
            object? propertyObject = properties[index];
            dynamic property = propertyObject;
            if ((int)property.PropertyID == propertyId)
            {
                return propertyObject;
            }

            ReleaseComObject(propertyObject);
        }

        return null;
    }

    private static void SetProperty(dynamic properties, int propertyId, int value, bool required = false)
    {
        object? propertyObject = FindProperty(properties, propertyId);
        if (propertyObject is null)
        {
            if (required)
            {
                throw new InvalidOperationException("Драйвер сканера не поддерживает выбранный параметр.");
            }

            return;
        }

        try
        {
            dynamic property = propertyObject;
            property.Value = value;
        }
        catch (Exception ex) when (!required)
        {
            _ = ex;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Сканер не принял выбранное разрешение или режим подачи.", ex);
        }
        finally
        {
            ReleaseComObject(propertyObject);
        }
    }

    private static void SetPropertyToMaximum(dynamic properties, int propertyId)
    {
        object? propertyObject = FindProperty(properties, propertyId);
        if (propertyObject is null)
        {
            return;
        }

        try
        {
            dynamic property = propertyObject;
            var maximum = ReadSubtypeBound(propertyObject, maximum: true);
            if (maximum is not null)
            {
                property.Value = maximum.Value;
            }
        }
        catch
        {
            // The driver can select the full scan area itself.
        }
        finally
        {
            ReleaseComObject(propertyObject);
        }
    }

    private static int? ReadSubtypeBound(object propertyObject, bool maximum)
    {
        try
        {
            dynamic property = propertyObject;
            return maximum ? Convert.ToInt32(property.SubTypeMax) : Convert.ToInt32(property.SubTypeMin);
        }
        catch
        {
            return null;
        }
    }

    private static Exception CreateFriendlyScanException(COMException exception)
    {
        var code = unchecked((uint)exception.HResult);
        var message = code switch
        {
            WiaErrorPaperJam => "В автоподатчике замялась бумага.",
            WiaErrorPaperEmpty => "В автоподатчике не обнаружены листы.",
            WiaErrorOffline => "Сканер выключен или не подключён.",
            WiaErrorBusy => "Сканер занят другой операцией.",
            WiaErrorCoverOpen => "Крышка или автоподатчик сканера открыт.",
            _ => $"Сканер сообщил об ошибке: {exception.Message}"
        };

        return new InvalidOperationException(message, exception);
    }

    private static Task<T> RunStaAsync<T>(Func<T> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(operation());
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "UISI WIA Scanner"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try
            {
                Marshal.FinalReleaseComObject(value);
            }
            catch
            {
                // COM cleanup is best effort; the STA thread is ending anyway.
            }
        }
    }
}
