// Minimal stand-ins for the Windows Runtime types RecordDocumentReader.cs uses through the
// Microsoft.Windows.SDK.Contracts package. Compile-only: nothing here runs.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Windows.Foundation
{
    public interface IAsyncAction { }
    public interface IAsyncOperation<TResult> { }
    public interface IAsyncOperationWithProgress<TResult, TProgress> : IAsyncOperation<TResult> { }
    public interface IAsyncActionWithProgress<TProgress> : IAsyncAction { }
    public struct Rect { public double X { get; set; } public double Y { get; set; } public double Width { get; set; } public double Height { get; set; } }
    public struct Size { public double Width { get; set; } public double Height { get; set; } }
}

namespace System
{
    public static class WindowsRuntimeSystemExtensions
    {
        public static Task AsTask(this global::Windows.Foundation.IAsyncAction source) { return Task.CompletedTask; }
        public static Task<TResult> AsTask<TResult>(this global::Windows.Foundation.IAsyncOperation<TResult> source) { return Task.FromResult(default(TResult)); }
    }
}

namespace System.IO
{
    public static class WindowsRuntimeStreamExtensions
    {
        public static global::Windows.Storage.Streams.IRandomAccessStream AsRandomAccessStream(this Stream stream) { return new global::Windows.Storage.Streams.InMemoryRandomAccessStream(); }
        public static Stream AsStream(this global::Windows.Storage.Streams.IRandomAccessStream stream) { return new MemoryStream(); }
        public static Stream AsStreamForRead(this global::Windows.Storage.Streams.IInputStream stream) { return new MemoryStream(); }
    }
}

namespace System.Runtime.InteropServices.WindowsRuntime
{
    public static class WindowsRuntimeBufferExtensions
    {
        public static global::Windows.Storage.Streams.IBuffer AsBuffer(this byte[] source) { return null; }
        public static byte[] ToArray(this global::Windows.Storage.Streams.IBuffer source) { return new byte[0]; }
    }
}

namespace Windows.Storage
{
    public interface IStorageFile { string Path { get; } string Name { get; } }
    public sealed class StorageFile : IStorageFile
    {
        public string Path { get; }
        public string Name { get; }
        public static Windows.Foundation.IAsyncOperation<StorageFile> GetFileFromPathAsync(string path) { return null; }
        public Windows.Foundation.IAsyncOperation<Streams.IRandomAccessStream> OpenAsync(FileAccessMode mode) { return null; }
    }
    public enum FileAccessMode { Read = 0, ReadWrite = 1 }
}

namespace Windows.Storage.Streams
{
    public interface IBuffer { uint Capacity { get; } uint Length { get; set; } }
    public interface IInputStream : IDisposable { }
    public interface IOutputStream : IDisposable { }
    public interface IRandomAccessStream : IInputStream, IOutputStream
    {
        ulong Size { get; set; }
        ulong Position { get; }
        bool CanRead { get; }
        bool CanWrite { get; }
        IInputStream GetInputStreamAt(ulong position);
        IOutputStream GetOutputStreamAt(ulong position);
        void Seek(ulong position);
    }
    public sealed class InMemoryRandomAccessStream : IRandomAccessStream
    {
        public ulong Size { get; set; }
        public ulong Position { get; }
        public bool CanRead { get { return true; } }
        public bool CanWrite { get { return true; } }
        public IInputStream GetInputStreamAt(ulong position) { return this; }
        public IOutputStream GetOutputStreamAt(ulong position) { return this; }
        public void Seek(ulong position) { }
        public void Dispose() { }
    }
    public sealed class DataReader : IDisposable
    {
        public DataReader(IInputStream stream) { }
        public Windows.Foundation.IAsyncOperation<uint> LoadAsync(uint count) { return null; }
        public void ReadBytes(byte[] value) { }
        public uint UnconsumedBufferLength { get; }
        public void Dispose() { }
    }
}

namespace Windows.Data.Pdf
{
    public sealed class PdfDocument
    {
        public static Windows.Foundation.IAsyncOperation<PdfDocument> LoadFromFileAsync(Windows.Storage.IStorageFile file) { return null; }
        public static Windows.Foundation.IAsyncOperation<PdfDocument> LoadFromFileAsync(Windows.Storage.IStorageFile file, string password) { return null; }
        public static Windows.Foundation.IAsyncOperation<PdfDocument> LoadFromStreamAsync(Windows.Storage.Streams.IRandomAccessStream stream) { return null; }
        public uint PageCount { get; }
        public bool IsPasswordProtected { get; }
        public PdfPage GetPage(uint index) { return new PdfPage(); }
    }
    public sealed class PdfPage : IDisposable
    {
        public Windows.Foundation.Size Size { get; }
        public Windows.Foundation.Rect Dimensions { get; }
        public uint Index { get; }
        public PdfPageRotation Rotation { get; }
        public float PreferredZoom { get; }
        public Windows.Foundation.IAsyncAction RenderToStreamAsync(Windows.Storage.Streams.IRandomAccessStream stream) { return null; }
        public Windows.Foundation.IAsyncAction RenderToStreamAsync(Windows.Storage.Streams.IRandomAccessStream stream, PdfPageRenderOptions options) { return null; }
        public Windows.Foundation.IAsyncAction PreparePageAsync() { return null; }
        public void Dispose() { }
    }
    public enum PdfPageRotation { Normal = 0, Rotate90 = 1, Rotate180 = 2, Rotate270 = 3 }
    public sealed class PdfPageRenderOptions
    {
        public uint DestinationWidth { get; set; }
        public uint DestinationHeight { get; set; }
        public Windows.Foundation.Rect SourceRect { get; set; }
        public bool IsIgnoringHighContrast { get; set; }
        public Guid BitmapEncoderId { get; set; }
        public Windows.UI.Color BackgroundColor { get; set; }
    }
}

namespace Windows.UI { public struct Color { public byte A { get; set; } public byte R { get; set; } public byte G { get; set; } public byte B { get; set; } } }

namespace Windows.Graphics.Imaging
{
    public enum BitmapPixelFormat { Unknown = 0, Rgba16 = 12, Rgba8 = 30, Gray16 = 57, Gray8 = 62, Bgra8 = 87, Nv12 = 103, P010 = 104, Yuy2 = 107 }
    public enum BitmapAlphaMode { Premultiplied = 0, Straight = 1, Ignore = 2 }
    public sealed class BitmapDecoder
    {
        public static Windows.Foundation.IAsyncOperation<BitmapDecoder> CreateAsync(Windows.Storage.Streams.IRandomAccessStream stream) { return null; }
        public static Windows.Foundation.IAsyncOperation<BitmapDecoder> CreateAsync(Guid decoderId, Windows.Storage.Streams.IRandomAccessStream stream) { return null; }
        public uint PixelWidth { get; }
        public uint PixelHeight { get; }
        public double DpiX { get; }
        public double DpiY { get; }
        public Windows.Foundation.IAsyncOperation<SoftwareBitmap> GetSoftwareBitmapAsync() { return null; }
        public Windows.Foundation.IAsyncOperation<SoftwareBitmap> GetSoftwareBitmapAsync(BitmapPixelFormat format, BitmapAlphaMode alpha) { return null; }
    }
    public sealed class SoftwareBitmap : IDisposable
    {
        public SoftwareBitmap(BitmapPixelFormat format, int width, int height) { PixelWidth = width; PixelHeight = height; }
        public SoftwareBitmap(BitmapPixelFormat format, int width, int height, BitmapAlphaMode alpha) : this(format, width, height) { }
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public BitmapPixelFormat BitmapPixelFormat { get; }
        public void CopyFromBuffer(Windows.Storage.Streams.IBuffer buffer) { }
        public void CopyToBuffer(Windows.Storage.Streams.IBuffer buffer) { }
        public static SoftwareBitmap Convert(SoftwareBitmap source, BitmapPixelFormat format) { return source; }
        public static SoftwareBitmap Convert(SoftwareBitmap source, BitmapPixelFormat format, BitmapAlphaMode alpha) { return source; }
        public void Dispose() { }
    }
}

namespace Windows.Globalization
{
    public sealed class Language
    {
        public Language(string languageTag) { LanguageTag = languageTag; }
        public string LanguageTag { get; }
        public string DisplayName { get { return LanguageTag; } }
        public static bool IsWellFormed(string languageTag) { return true; }
    }
}

namespace Windows.Media.Ocr
{
    public sealed class OcrEngine
    {
        public static uint MaxImageDimension { get { return 2600; } }
        public static IReadOnlyList<Windows.Globalization.Language> AvailableRecognizerLanguages { get { return new List<Windows.Globalization.Language>(); } }
        public static bool IsLanguageSupported(Windows.Globalization.Language language) { return true; }
        public static OcrEngine TryCreateFromLanguage(Windows.Globalization.Language language) { return null; }
        public static OcrEngine TryCreateFromUserProfileLanguages() { return null; }
        public Windows.Globalization.Language RecognizerLanguage { get; }
        public Windows.Foundation.IAsyncOperation<OcrResult> RecognizeAsync(Windows.Graphics.Imaging.SoftwareBitmap bitmap) { return null; }
    }
    public sealed class OcrResult { public IReadOnlyList<OcrLine> Lines { get; } public string Text { get; } public double? TextAngle { get; } }
    public sealed class OcrLine { public IReadOnlyList<OcrWord> Words { get; } public string Text { get; } }
    public sealed class OcrWord { public Windows.Foundation.Rect BoundingRect { get; } public string Text { get; } }
}
