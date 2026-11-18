using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.HumanInterfaceDevice;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace WinTools.Platform.Battery;

/// <summary>One HID top-level collection, as Windows exposes it (one device path per collection).</summary>
public sealed record HidInterface(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputLength,
    int OutputLength,
    int FeatureLength,
    string Product);

public static class HidInterfaces
{
    private static readonly NTSTATUS HidpStatusSuccess = new(0x00110000);

    /// <summary>Lists present HID collections. Collections the system holds exclusively still open with zero access.</summary>
    public static IReadOnlyList<HidInterface> Enumerate()
    {
        var result = new List<HidInterface>();
        foreach (var path in DevicePaths())
        {
            using var handle = Open(path, 0, overlapped: false);
            if (handle.IsInvalid)
            {
                continue;
            }
            if (Describe(path, handle) is { } hid)
            {
                result.Add(hid);
            }
        }
        return result;
    }

    /// <summary>Opens a collection for report I/O; returns an invalid handle when access is denied.</summary>
    public static SafeFileHandle Open(string path, uint access, bool overlapped) =>
        PInvoke.CreateFile(
            path,
            access,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            overlapped ? FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED : 0,
            null);

    public static unsafe bool SetFeature(SafeFileHandle handle, byte[] report)
    {
        fixed (byte* p = report)
        {
            return PInvoke.HidD_SetFeature(Raw(handle), p, (uint)report.Length);
        }
    }

    public static unsafe bool GetFeature(SafeFileHandle handle, byte[] report)
    {
        fixed (byte* p = report)
        {
            return PInvoke.HidD_GetFeature(Raw(handle), p, (uint)report.Length);
        }
    }

    private static unsafe HidInterface? Describe(string path, SafeFileHandle handle)
    {
        HIDD_ATTRIBUTES attributes;
        if (!PInvoke.HidD_GetAttributes(handle, out attributes))
        {
            return null;
        }
        if (!PInvoke.HidD_GetPreparsedData(handle, out var preparsed))
        {
            return null;
        }
        HIDP_CAPS caps;
        try
        {
            if (PInvoke.HidP_GetCaps(preparsed, out caps) != HidpStatusSuccess)
            {
                return null;
            }
        }
        finally
        {
            PInvoke.HidD_FreePreparsedData(preparsed);
        }

        var product = stackalloc char[127];
        var name = PInvoke.HidD_GetProductString(Raw(handle), product, 127 * sizeof(char))
            ? new string(product)
            : "";

        return new HidInterface(
            path, attributes.VendorID, attributes.ProductID, caps.UsagePage, caps.Usage,
            caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength, name.Trim());
    }

    // Callers hold the SafeFileHandle in a using scope for the duration of the call.
    private static HANDLE Raw(SafeFileHandle handle) => (HANDLE)handle.DangerousGetHandle();

    private static unsafe List<string> DevicePaths()
    {
        PInvoke.HidD_GetHidGuid(out var hidGuid);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (PInvoke.CM_Get_Device_Interface_List_Size(out var length, hidGuid, null,
                    CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS)
            {
                return [];
            }
            var buffer = new char[length];
            fixed (char* p = buffer)
            {
                var status = PInvoke.CM_Get_Device_Interface_List(&hidGuid, null, p, length,
                    CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
                if (status == CONFIGRET.CR_BUFFER_SMALL)
                {
                    continue; // a device arrived between the two calls
                }
                if (status != CONFIGRET.CR_SUCCESS)
                {
                    return [];
                }
            }
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return [];
    }
}
