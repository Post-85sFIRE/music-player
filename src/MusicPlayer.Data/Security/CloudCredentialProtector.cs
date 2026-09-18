using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace MusicPlayer.Data;

/// <summary>
/// 云盘密码本地加密存储（参考鸿蒙 Asset Store Kit 的"硬件级/设备级加密、不落明文"思路）。
/// Windows 端等价实现：DPAPI（Crypt32.dll 的 CryptProtectData / CryptUnprotectData），
/// 密钥由当前用户登录凭据派生（等价于鸿蒙的 DEVICE_FIRST_UNLOCKED：本机该用户解锁后可读），
/// 其他用户/其他机器无法解密，卸载即随用户配置文件清除。
/// 仅在 Windows 上可用；任何异常（如非 Windows 环境或数据损坏）均兜底返回原值，绝不影响连接流程。
/// </summary>
internal static class CloudCredentialProtector
{
    // CRYPTPROTECT_UI_FORBIDDEN：禁止弹出 UI，静默使用用户凭据加密。
    private const int CrpProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int CbData;
        public IntPtr PbData;
    }

    private delegate bool CryptOp(ref DataBlob pDataIn, ref DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>加密明文密码为可持久化的字符串（Base64）。空值原样返回。</summary>
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        try
        {
            var inBytes = Encoding.UTF8.GetBytes(plain);
            var outBytes = Transform(inBytes, (ref DataBlob i, ref DataBlob o) => CryptProtectData(ref i, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CrpProtectUiForbidden, ref o));
            return Convert.ToBase64String(outBytes);
        }
        catch
        {
            // 加密失败（非 Windows / 凭据不可用等）：兜底保留明文，不阻断保存。
            return plain;
        }
    }

    /// <summary>解密为明文密码。若输入不是本 protector 产生的密文（如旧版明文），原样返回以便兼容迁移。</summary>
    public static string Unprotect(string cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return cipher;
        try
        {
            var inBytes = Convert.FromBase64String(cipher);
            var outBytes = Transform(inBytes, (ref DataBlob i, ref DataBlob o) => CryptUnprotectData(ref i, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CrpProtectUiForbidden, ref o));
            return Encoding.UTF8.GetString(outBytes);
        }
        catch
        {
            // 不是合法密文（旧版明文密码）或解密失败 → 原样返回，下次保存时会被重新加密。
            return cipher;
        }
    }

    private static byte[] Transform(byte[] data, CryptOp op)
    {
        var inBlob = new DataBlob { CbData = data.Length, PbData = Marshal.AllocHGlobal(data.Length) };
        Marshal.Copy(data, 0, inBlob.PbData, data.Length);
        var outBlob = new DataBlob();
        try
        {
            if (!op(ref inBlob, ref outBlob))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[outBlob.CbData];
            Marshal.Copy(outBlob.PbData, result, 0, outBlob.CbData);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.PbData);
            if (outBlob.PbData != IntPtr.Zero) LocalFree(outBlob.PbData);
        }
    }
}
