using NUnit.Framework;
using OxGFrame.AssetLoader.Utility;
using System;
using System.Diagnostics;
using System.IO;
using static OxGFrame.AssetLoader.Bundle.FileCryptogram;

namespace OxGFrame.AssetLoader.Editor.Tests
{
    public class XXTEATests
    {
        internal readonly string key = "8F395535C4BA65A9E3EFC7C6BDDC1";

        [Test]
        public void EncryptDecryptBytesFromData()
        {
            Stopwatch stopwatch = new Stopwatch();

            byte[] testBytes = new byte[CryptogramConfig.DATA_SIZE];
            new Random().NextBytes(testBytes);
            byte[] originalBytes = (byte[])testBytes.Clone();

            stopwatch.Start();
            bool encryptResult = XXTEA.EncryptBytes(ref testBytes, key);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"[EncryptDecryptBytesFromData] XXTEA.EncryptBytes execution time: {stopwatch.Elapsed.TotalMilliseconds} ms, CryptogramConfig.DATA_SIZE: {BundleUtility.GetBytesToString(CryptogramConfig.DATA_SIZE)}");
            Assert.IsTrue(encryptResult, "In-place encryption failed");

            stopwatch.Reset();

            stopwatch.Start();
            bool decryptResult = XXTEA.DecryptBytes(ref testBytes, key);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"[EncryptDecryptBytesFromData] XXTEA.DecryptBytes execution time: {stopwatch.Elapsed.TotalMilliseconds} ms, CryptogramConfig.DATA_SIZE: {BundleUtility.GetBytesToString(CryptogramConfig.DATA_SIZE)}");
            Assert.IsTrue(decryptResult, "In-place decryption failed");

            Assert.AreEqual(originalBytes, testBytes, "Decrypted content does not match the original content");
        }

        [Test]
        public void EncryptDecryptWriteFile()
        {
            Stopwatch stopwatch = new Stopwatch();

            string tempFile = Path.GetTempFileName();
            byte[] testData = new byte[CryptogramConfig.DATA_SIZE];
            new Random().NextBytes(testData);
            File.WriteAllBytes(tempFile, testData);

            stopwatch.Start();
            bool encryptResult = XXTEA.WriteFile.EncryptFile(tempFile, key);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"[EncryptDecryptWriteFile] XXTEA.WriteFile.EncryptFile execution time: {stopwatch.Elapsed.TotalMilliseconds} ms, CryptogramConfig.DATA_SIZE: {BundleUtility.GetBytesToString(CryptogramConfig.DATA_SIZE)}");
            Assert.IsTrue(encryptResult, "File encryption failed");

            stopwatch.Reset();

            stopwatch.Start();
            bool decryptResult = XXTEA.WriteFile.DecryptFile(tempFile, key);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"[EncryptDecryptWriteFile] XXTEA.WriteFile.DecryptFile execution time: {stopwatch.Elapsed.TotalMilliseconds} ms, CryptogramConfig.DATA_SIZE: {BundleUtility.GetBytesToString(CryptogramConfig.DATA_SIZE)}");
            Assert.IsTrue(decryptResult, "File decryption failed");

            byte[] decryptedData = File.ReadAllBytes(tempFile);
            Assert.AreEqual(testData, decryptedData, "Decrypted file content does not match the original content");

            File.Delete(tempFile);
        }

        [Test]
        public void EncryptDecryptBytesFromFile()
        {
            Stopwatch stopwatch = new Stopwatch();

            string tempFile = Path.GetTempFileName();
            byte[] testData = new byte[CryptogramConfig.DATA_SIZE];
            new Random().NextBytes(testData);
            File.WriteAllBytes(tempFile, testData);

            stopwatch.Start();
            byte[] encryptedBytes = XXTEA.EncryptBytes(tempFile, key);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"[EncryptDecryptBytesFromFile] XXTEA.EncryptBytes execution time: {stopwatch.Elapsed.TotalMilliseconds} ms, CryptogramConfig.DATA_SIZE: {BundleUtility.GetBytesToString(CryptogramConfig.DATA_SIZE)}");
            Assert.IsNotNull(encryptedBytes, "Encrypted bytes returned null");
            Assert.IsNotEmpty(encryptedBytes, "Encrypted bytes are empty");

            stopwatch.Reset();

            string encryptedFile = tempFile + ".enc";
            File.WriteAllBytes(encryptedFile, encryptedBytes);

            stopwatch.Start();
            byte[] decryptedBytes = XXTEA.DecryptBytes(encryptedFile, key);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"[EncryptDecryptBytesFromFile] XXTEA.DecryptBytes execution time: {stopwatch.Elapsed.TotalMilliseconds} ms, CryptogramConfig.DATA_SIZE: {BundleUtility.GetBytesToString(CryptogramConfig.DATA_SIZE)}");
            Assert.IsNotNull(decryptedBytes, "Decrypted bytes returned null");
            Assert.AreEqual(testData, decryptedBytes, "Decrypted content does not match the original content");

            File.Delete(tempFile);
            File.Delete(encryptedFile);
        }

        [Test]
        public void DecryptStream()
        {
            Stopwatch stopwatch = new Stopwatch();

            string tempFile = Path.GetTempFileName();
            byte[] testData = new byte[CryptogramConfig.DATA_SIZE];
            new Random().NextBytes(testData);
            File.WriteAllBytes(tempFile, testData);


            bool encryptResult = XXTEA.WriteFile.EncryptFile(tempFile, key);
            Assert.IsTrue(encryptResult, "File encryption failed");

            stopwatch.Start();
            using (Stream decryptedStream = XXTEA.DecryptStream(tempFile, key))
            {
                stopwatch.Stop();
                Assert.IsNotNull(decryptedStream, "Decrypted stream is null");
                using (MemoryStream ms = new MemoryStream())
                {
                    decryptedStream.CopyTo(ms);
                    byte[] decryptedData = ms.ToArray();
                    Assert.AreEqual(testData, decryptedData, "Stream decrypted content does not match");
                }
            }
            UnityEngine.Debug.Log($"[DecryptStream] XXTEA.DecryptStream execution time: {stopwatch.Elapsed.TotalMilliseconds} ms, CryptogramConfig.DATA_SIZE: {BundleUtility.GetBytesToString(CryptogramConfig.DATA_SIZE)}");

            File.Delete(tempFile);
        }
    }
}
