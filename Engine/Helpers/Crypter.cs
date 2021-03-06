﻿using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Engine.Helpers
{
  [SecuritySafeCritical]
  public sealed class Crypter : IDisposable
  {
    #region Nested types
    /// <summary>
    /// CryptoStream closes outputStream, this class avoids this behaviour
    /// </summary>
    /// <typeparam name="TStream">Real stream type</typeparam>
    private class NotDisposableStream<TStream> : Stream
      where TStream: Stream
    {
      private TStream real;

      public NotDisposableStream(TStream stream)
      {
        if (stream == null)
          throw new ArgumentNullException("stream");
        real = stream;
      }

      public override bool CanRead { get { return real.CanRead; } }
      public override bool CanSeek { get { return real.CanSeek; } }
      public override bool CanWrite { get { return real.CanWrite; } }
      public override long Length { get { return real.Length; } }

      public override long Position
      {
        get { return real.Position; }
        set { real.Position = value; }
      }

      public override void Flush() { real.Flush(); }
      public override int Read(byte[] buffer, int offset, int count) { return real.Read(buffer, offset, count); }
      public override long Seek(long offset, SeekOrigin origin) { return real.Seek(offset, origin); }
      public override void SetLength(long value) { real.SetLength(value); }
      public override void Write(byte[] buffer, int offset, int count) { real.Write(buffer, offset, count); }
    }
    #endregion

    #region Constats
    private const int BufferSize = 4096;
    #endregion

    #region Private Values
    private SymmetricAlgorithm algorithm;
    private bool disposed;
    #endregion

    #region Constructors
    /// <summary>
    /// Создает экемпляр класса Crypter. C алгоритмом AES-256
    /// </summary>
    public Crypter()
    {
      algorithm = new AesCryptoServiceProvider()
      {
        KeySize = 256,
        Mode = CipherMode.CBC,
        Padding = PaddingMode.PKCS7
      };
    }

    /// <summary>
    /// Создает экемпляр класса Crypter.
    /// </summary>
    /// <param name="symmetricAlg">Алгоритм шифрования.</param>
    [SecuritySafeCritical]
    public Crypter(SymmetricAlgorithm symmetricAlg)
    {
      if (symmetricAlg == null)
        throw new ArgumentNullException();

      algorithm = symmetricAlg;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Генерирует ключ и вектор инициализации.
    /// </summary>
    /// <returns>Ключ.</returns>
    [SecuritySafeCritical]
    public byte[] GenerateKey()
    {
      ThrowIfDisposed();

      algorithm.GenerateKey();
      algorithm.GenerateIV();

      return algorithm.Key;
    }

    /// <summary>
    /// Устанавливает ключ и генерирует вектор инициализации.
    /// </summary>
    /// <param name="key">Ключ шифрования.</param>
    [SecuritySafeCritical]
    public void SetKey(byte[] key)
    {
      ThrowIfDisposed();

      algorithm.Key = key;
      algorithm.GenerateIV();
    }

    /// <summary>
    /// Производит шифрование потока.
    /// </summary>
    /// <param name="inputStream">Поток, который будет зашифрован.</param>
    /// <param name="outputStream">Поток, в который будет записан результат шифрования.</param>
    [SecuritySafeCritical]
    public void Encrypt(Stream inputStream, Stream outputStream)
    {
      ThrowIfDisposed();

      if (inputStream == null)
        throw new ArgumentNullException("Input stream is null");

      if (outputStream == null)
        throw new ArgumentNullException("Output stream is null");

      var outputWrapper = new NotDisposableStream<Stream>(outputStream);

      using (var transform = algorithm.CreateEncryptor())
      using (var encrypter = new CryptoStream(outputWrapper, transform, CryptoStreamMode.Write))
      using (var writer = new BinaryWriter(outputWrapper, Encoding.Unicode, true))
      {
        writer.Write(algorithm.IV);

        var dataBuffer = new byte[BufferSize];

        while (inputStream.Position < inputStream.Length)
        {
          var readed = inputStream.Read(dataBuffer, 0, BufferSize);
          encrypter.Write(dataBuffer, 0, readed);
        }
      }
    }

    /// <summary>
    /// Шифрует объект в поток.
    /// </summary>
    /// <typeparam name="T">Тип объекта, который будет зашифрован.</typeparam>
    /// <param name="obj">Объект который будет зашифрован.</param>
    /// <param name="outputStream">Поток в который будет зашифрован объект.</param>
    [SecuritySafeCritical]
    public void Encrypt<T>(T obj, Stream outputStream)
    {
      ThrowIfDisposed();

      if (obj == null)
        throw new ArgumentNullException("Input object is null");

      if (outputStream == null)
        throw new ArgumentNullException("Output stream is null");

      var outputWrapper = new NotDisposableStream<Stream>(outputStream);

      using (var transform = algorithm.CreateEncryptor())
      using (var encrypter = new CryptoStream(outputWrapper, transform, CryptoStreamMode.Write))
      using (var writer = new BinaryWriter(outputWrapper, Encoding.Unicode, true))
      {
        writer.Write(algorithm.IV);
        Serializer.Serialize(encrypter, obj);
      }
    }

    /// <summary>
    /// Шифрует объект в массив байт.
    /// </summary>
    /// <typeparam name="T">Тип объекта, который будет зашифрован.</typeparam>
    /// <param name="obj">Объект который будет зашифрован.</param>
    /// <returns>Зашифрованный объект.</returns>
    [SecuritySafeCritical]
    public MemoryStream Encrypt<T>(T obj)
    {
      ThrowIfDisposed();

      var outputStream = new MemoryStream();
      Encrypt(obj, outputStream);
      outputStream.Seek(0, SeekOrigin.Begin);
      return outputStream;
    }

    /// <summary>
    /// Производит дешифрование потока.
    /// </summary>
    /// <param name="inputStream">Поток, который будет дешифрован.</param>
    /// <param name="outputStream">Поток, в который будет записан результат дешифрования.</param>
    /// <param name="key">Ключ для дешифрования.</param>
    [SecuritySafeCritical]
    public void Decrypt(Stream inputStream, Stream outputStream)
    {
      ThrowIfDisposed();

      if (inputStream == null)
        throw new ArgumentNullException("Input stream is null");

      if (outputStream == null)
        throw new ArgumentNullException("Output stream is null");

      using (var reader = new BinaryReader(inputStream, Encoding.Unicode, true))
      {
        algorithm.IV = reader.ReadBytes(algorithm.BlockSize / 8);

        using (var transform = algorithm.CreateDecryptor())
        using (var decryptor = new CryptoStream(inputStream, transform, CryptoStreamMode.Read))
        {
          var dataBuffer = new byte[BufferSize];

          while (inputStream.Position < inputStream.Length)
          {
            var readed = decryptor.Read(dataBuffer, 0, BufferSize);
            outputStream.Write(dataBuffer, 0, readed);
          }
        }
      }
    }

    /// <summary>
    /// Расшифровывает из потока 1 объект.
    /// </summary>
    /// <typeparam name="T">Тип объекта который будет расшифрован.</typeparam>
    /// <param name="inputStream">Поток с входными данными.</param>
    /// <returns>Расшифрованый объект.</returns>
    [SecuritySafeCritical]
    public T Decrypt<T>(Stream inputStream)
    {
      ThrowIfDisposed();

      if (inputStream == null)
        throw new ArgumentNullException("Input stream is null");

      using (var reader = new BinaryReader(inputStream, Encoding.Unicode, true))
      {
        algorithm.IV = reader.ReadBytes(algorithm.BlockSize / 8);

        using (var transform = algorithm.CreateDecryptor())
        using (var decryptor = new CryptoStream(inputStream, transform, CryptoStreamMode.Read))
          return Serializer.Deserialize<T>(decryptor);
      }
    }

    /// <summary>
    /// Расшифровывает из потока 1 объект.
    /// </summary>
    /// <typeparam name="T">Тип объекта который будет расшифрован.</typeparam>
    /// <param name="input">Массив с входными данными.</param>
    /// <returns>Расшифрованый объект.</returns>
    [SecuritySafeCritical]
    public T Decrypt<T>(byte[] input)
    {
      ThrowIfDisposed();

      using (var inputStream = new MemoryStream(input))
        return Decrypt<T>(inputStream);
    }
    #endregion

    #region IDisposable
    [SecuritySafeCritical]
    private void ThrowIfDisposed()
    {
      if (disposed)
        throw new ObjectDisposedException("Object Disposed");
    }

    [SecuritySafeCritical]
    public void Dispose()
    {
      if (disposed == true)
        return;

      algorithm.Clear();

      disposed = true;
    }
    #endregion
  }
}
