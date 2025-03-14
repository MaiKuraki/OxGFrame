﻿using System;
using System.IO;
using System.Collections.Generic;

namespace YooAsset
{
    /// <summary>
    /// 内置资源清单目录
    /// </summary>
    [Serializable]
    internal class DefaultBuildinFileCatalog
    {
        [Serializable]
        public class FileWrapper
        {
            public string BundleGUID;
            public string FileName;

            public FileWrapper(string bundleGUID, string fileName)
            {
                BundleGUID = bundleGUID;
                FileName = fileName;
            }
        }

        /// <summary>
        /// 包裹名称
        /// </summary>
        public string PackageName;

        /// <summary>
        /// 包裹版本
        /// </summary>
        public string PackageVersion;

        /// <summary>
        /// 文件列表
        /// </summary>
        public List<FileWrapper> Wrappers = new List<FileWrapper>();
    }
}