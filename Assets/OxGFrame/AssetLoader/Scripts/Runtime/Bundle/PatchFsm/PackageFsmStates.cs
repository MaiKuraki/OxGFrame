﻿using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using OxGFrame.AssetLoader.Bundle;
using OxGFrame.AssetLoader.PatchEvent;
using OxGFrame.AssetLoader.Utility;
using OxGKit.LoggingSystem;
using System;
using System.Collections.Generic;
using UniFramework.Machine;
using YooAsset;

namespace OxGFrame.AssetLoader.PatchFsm
{
    public static class PackageFsmStates
    {
        /// <summary>
        /// 0. 修復流程
        /// </summary>
        public class FsmPatchRepair : IStateNode
        {
            private StateMachine _machine;
            private int _hashId;
            private int _retryCount = _RETRY_COUNT;

            private const int _RETRY_COUNT = 1;

            public FsmPatchRepair() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
                this._hashId = (this._machine.Owner as PackageOperation).hashId;
            }

            void IStateNode.OnEnter()
            {
                // 流程準備
                PackageEvents.PatchFsmState.SendEventMessage(this._hashId, this);
                (this._machine.Owner as PackageOperation).MarkRepairState();
                this._DeleteLocalSaveFiles().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _DeleteLocalSaveFiles()
            {
                // EditorSimulateMode skip repair
                if (BundleConfig.playMode == BundleConfig.PlayMode.EditorSimulateMode)
                {
                    this._machine.ChangeState<FsmPatchPrepare>();
                    return;
                }

                // Cancel main download first
                (this._machine.Owner as PackageOperation).Cancel(false);

                // Wait a frame
                await UniTask.NextFrame();

                // Get package names
                var packageNames = (this._machine.Owner as PackageOperation).GetPackageNames();

                bool isCleared = false;
                foreach (var packageName in packageNames)
                {
                    // Clear cache and files of package
                    isCleared = await PackageManager.UnloadPackageAndClearCacheFiles(packageName, false);
                    if (!isCleared) break;
                }

                if (isCleared || this._retryCount <= 0)
                {
                    this._retryCount = _RETRY_COUNT;
                    this._machine.ChangeState<FsmPatchPrepare>();
                }
                else
                {
                    this._retryCount--;
                    PackageEvents.PatchRepairFailed.SendEventMessage(this._hashId);
                }
            }
        }

        /// <summary>
        /// 1. 流程準備工作
        /// </summary>
        public class FsmPatchPrepare : IStateNode
        {
            private StateMachine _machine;
            private int _hashId;

            public FsmPatchPrepare() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
                this._hashId = (this._machine.Owner as PackageOperation).hashId;
            }

            void IStateNode.OnEnter()
            {
                // 流程準備
                PackageEvents.PatchFsmState.SendEventMessage(this._hashId, this);
                (this._machine.Owner as PackageOperation).MarkReadyAsDone();
                this._machine.ChangeState<FsmInitPatchMode>();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }
        }

        /// <summary>
        /// 2. 初始 Patch Mode
        /// </summary>
        public class FsmInitPatchMode : IStateNode
        {
            private StateMachine _machine;
            private int _hashId;

            public FsmInitPatchMode() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
                this._hashId = (this._machine.Owner as PackageOperation).hashId;
            }

            void IStateNode.OnEnter()
            {
                // 初始更新資源配置
                PackageEvents.PatchFsmState.SendEventMessage(this._hashId, this);
                this._InitPatchMode().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _InitPatchMode()
            {
                await UniTask.Delay(TimeSpan.FromSeconds(0.1f), true);

                var packageInfos = (this._machine.Owner as PackageOperation).GetPackageInfos();
                if (packageInfos != null)
                {
                    if (packageInfos.Length == 0)
                        throw new Exception("Package infos array is of zero.");

                    bool isInitialized = false;
                    foreach (var packageInfo in packageInfos)
                    {
                        string hostServer;
                        string fallbackHostServer;

                        if (packageInfo is DlcPackageInfoWithBuild)
                        {
                            var packageDetail = packageInfo as DlcPackageInfoWithBuild;
                            hostServer = packageDetail.hostServer;
                            fallbackHostServer = packageDetail.fallbackHostServer;

                            if (BundleConfig.playModeParameters.autoConfigureServerEndpoints)
                            {
                                hostServer = string.IsNullOrEmpty(hostServer) ? await BundleConfig.GetDlcHostServerUrl(packageDetail.packageName, packageDetail.dlcVersion, packageDetail.withoutPlatform) : hostServer;
                                fallbackHostServer = string.IsNullOrEmpty(fallbackHostServer) ? await BundleConfig.GetDlcFallbackHostServerUrl(packageDetail.packageName, packageDetail.dlcVersion, packageDetail.withoutPlatform) : fallbackHostServer;
                            }
                        }
                        else if (packageInfo is AppPackageInfoWithBuild)
                        {
                            var packageDetail = packageInfo as AppPackageInfoWithBuild;
                            hostServer = null;
                            fallbackHostServer = null;

                            if (BundleConfig.playModeParameters.autoConfigureServerEndpoints)
                            {
                                hostServer = await BundleConfig.GetHostServerUrl(packageDetail.packageName);
                                fallbackHostServer = await BundleConfig.GetFallbackHostServerUrl(packageDetail.packageName);
                            }
                        }
                        else throw new Exception("Package info type error.");

                        // Try-catch to avoid same package to init, will try-catch until is initialized
                        try
                        {
                            isInitialized = await PackageManager.InitPackage(packageInfo, false, hostServer, fallbackHostServer);
                        }
                        catch
                        {
                            isInitialized = false;
                        }

                        if (!isInitialized)
                            break;
                    }

                    if (isInitialized)
                    {
                        Logging.Print<Logger>("(Init) Init Patch");
                        this._machine.ChangeState<FsmPatchVersionUpdate>();
                    }
                    else
                    {
                        PackageEvents.PatchInitPatchModeFailed.SendEventMessage(this._hashId);
                    }
                }
                else throw new Exception("Cannot get package infos (NULL).");
            }
        }

        /// <summary>
        /// 3. 更新 Patch Version
        /// </summary>
        public class FsmPatchVersionUpdate : IStateNode
        {
            private StateMachine _machine;
            private int _hashId;

            public FsmPatchVersionUpdate() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
                this._hashId = (this._machine.Owner as PackageOperation).hashId;
            }

            void IStateNode.OnEnter()
            {
                // 獲取最新的資源版本
                PackageEvents.PatchFsmState.SendEventMessage(this._hashId, this);
                (this._machine.Owner as PackageOperation).MarkRepairAsDone();
                this._UpdatePatchVersion().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _UpdatePatchVersion()
            {
                await UniTask.Delay(TimeSpan.FromSeconds(0.1f), true);

                // Get packages
                var packages = (this._machine.Owner as PackageOperation).GetPackages();

                bool succeed = false;
                List<string> patchVersions = new List<string>();
                string currentPackageName = string.Empty;
                foreach (var package in packages)
                {
                    currentPackageName = package.PackageName;
                    var operation = package.RequestPackageVersionAsync();
                    await operation;

                    if (operation.Status == EOperationStatus.Succeed)
                    {
                        succeed = true;
                        patchVersions.Add(operation.PackageVersion);
                    }
                    else
                    {
                        succeed = false;
                        break;
                    }
                }

                if (succeed)
                {
                    this._machine.SetBlackboardValue(PackageOperation.KEY_PACKAGE_VERSIONS, patchVersions.ToArray());
                    this._machine.SetBlackboardValue(PackageOperation.KEY_IS_LAST_PACKAGE_VERSIONS, false);
                    this._machine.ChangeState<FsmPatchManifestUpdate>();
                }
                else
                {
                    #region Weak Host Mode
                    if (BundleConfig.playModeParameters.enableLastLocalVersionsCheckInWeakNetwork)
                    {
                        patchVersions.Clear();
                        foreach (var package in packages)
                        {
                            // 獲取上一次本地資源版號
                            string lastVersion = BundleConfig.saver.GetData(BundleConfig.LAST_PACKAGE_VERSIONS_KEY, package.PackageName, string.Empty);
                            if (string.IsNullOrEmpty(lastVersion))
                            {
                                PackageEvents.PatchVersionUpdateFailed.SendEventMessage(this._hashId);
                                Logging.PrintError<Logger>($"Package: {package.PackageName}. Local version record not found, resources need to be updated (Please connect to the network)!");
                                return;
                            }
                            patchVersions.Add(lastVersion);
                        }

                        this._machine.SetBlackboardValue(PackageOperation.KEY_PACKAGE_VERSIONS, patchVersions.ToArray());
                        this._machine.SetBlackboardValue(PackageOperation.KEY_IS_LAST_PACKAGE_VERSIONS, true);
                        this._machine.ChangeState<FsmPatchManifestUpdate>();
                    }
                    #endregion
                    else
                    {
                        PackageEvents.PatchVersionUpdateFailed.SendEventMessage(this._hashId);
                        Logging.PrintError<Logger>($"Package: {currentPackageName} update version failed.");
                    }
                }
            }
        }

        /// <summary>
        /// 4. 更新 Patch Manifest
        /// </summary>
        public class FsmPatchManifestUpdate : IStateNode
        {
            private StateMachine _machine;
            private int _hashId;

            public FsmPatchManifestUpdate() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
                this._hashId = (this._machine.Owner as PackageOperation).hashId;
            }

            void IStateNode.OnEnter()
            {
                // 更新資源清單
                PackageEvents.PatchFsmState.SendEventMessage(this._hashId, this);
                this._UpdatePatchManifest().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _UpdatePatchManifest()
            {
                await UniTask.Delay(TimeSpan.FromSeconds(0.1f), true);

                // Get packages
                var packages = (this._machine.Owner as PackageOperation).GetPackages();
                var packageVersions = (string[])this._machine.GetBlackboardValue(PackageOperation.KEY_PACKAGE_VERSIONS);

                bool succeed = false;
                string currentPackageName = string.Empty;
                for (int i = 0; i < packages.Length; i++)
                {
                    currentPackageName = packages[i].PackageName;
                    string version = packageVersions[i];
                    var operation = packages[i].UpdatePackageManifestAsync(packageVersions[i]);
                    await operation;

                    if (operation.Status == EOperationStatus.Succeed)
                    {
                        // 儲存本地資源版本
                        BundleConfig.saver.SaveData(BundleConfig.LAST_PACKAGE_VERSIONS_KEY, currentPackageName, version);
                        succeed = true;
                        Logging.PrintInfo<Logger>($"Package: {packages[i].PackageName} Update completed successfully.");
                    }
                    else
                    {
                        succeed = false;
                        break;
                    }
                }

                if (succeed)
                {
                    this._machine.ChangeState<FsmCreateDownloader>();
                }
                else
                {
                    #region Weak Host Mode
                    if (BundleConfig.playModeParameters.enableLastLocalVersionsCheckInWeakNetwork)
                    {
                        PackageEvents.PatchManifestUpdateFailed.SendEventMessage(this._hashId);
                        Logging.PrintError<Logger>($"Package: {currentPackageName}. Failed to load the local resource manifest file. Resource update is required (Please connect to the network)!");
                    }
                    #endregion
                    else
                    {
                        PackageEvents.PatchManifestUpdateFailed.SendEventMessage(this._hashId);
                        Logging.PrintError<Logger>($"Package: {currentPackageName} update manifest failed.");
                    }
                }
            }
        }

        /// <summary>
        /// 5. 創建資源下載器
        /// </summary>
        public class FsmCreateDownloader : IStateNode
        {
            private StateMachine _machine;
            private int _hashId;

            public FsmCreateDownloader() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
                this._hashId = (this._machine.Owner as PackageOperation).hashId;
            }

            void IStateNode.OnEnter()
            {
                // 創建資源下載器
                PackageEvents.PatchFsmState.SendEventMessage(this._hashId, this);
                this._CreateDownloader();
                (this._machine.Owner as PackageOperation).MarkReadyState();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private void _CreateDownloader()
            {
                // EditorSimulateMode skip directly
                if (BundleConfig.playMode == BundleConfig.PlayMode.EditorSimulateMode)
                {
                    this._machine.ChangeState<FsmPatchDone>();
                    return;
                }

                // 判斷目前是否獲取的是本地版號 (如果是的話, 表示目前處於弱聯網)
                bool isLastPackageVersions = Convert.ToBoolean(this._machine.GetBlackboardValue(PackageOperation.KEY_IS_LAST_PACKAGE_VERSIONS));

                bool skipDownload = (this._machine.Owner as PackageOperation).skipDownload;
                if (skipDownload && !isLastPackageVersions)
                {
                    this._machine.ChangeState<FsmDownloadOver>();
                    return;
                }

                #region Create Downloader by Tags
                // Get packages
                var packages = (this._machine.Owner as PackageOperation).GetPackages();
                var groupInfo = (this._machine.Owner as PackageOperation).groupInfo;

                // Reset group info
                groupInfo.totalCount = 0;
                groupInfo.totalBytes = 0;

                int totalDownloadCount;
                long totalDownloadBytes;
                for (int i = 0; i < packages.Length; i++)
                {
                    var package = packages[i];

                    // all package
                    if (groupInfo.tags == null || (groupInfo.tags != null && groupInfo.tags.Length == 0))
                    {
                        var downloader = package.CreateResourceDownloader(BundleConfig.maxConcurrencyDownloadCount, BundleConfig.failedRetryCount);
                        totalDownloadCount = downloader.TotalDownloadCount;
                        totalDownloadBytes = downloader.TotalDownloadBytes;
                        if (totalDownloadCount > 0)
                        {
                            groupInfo.totalCount += totalDownloadCount;
                            groupInfo.totalBytes += totalDownloadBytes;
                        }
                    }
                    // package by tags
                    else
                    {
                        var downloader = package.CreateResourceDownloader(groupInfo.tags, BundleConfig.maxConcurrencyDownloadCount, BundleConfig.failedRetryCount);
                        totalDownloadCount = downloader.TotalDownloadCount;
                        totalDownloadBytes = downloader.TotalDownloadBytes;
                        if (totalDownloadCount > 0)
                        {
                            groupInfo.totalCount += totalDownloadCount;
                            groupInfo.totalBytes += totalDownloadBytes;
                        }
                    }
                }
                #endregion

                if (groupInfo.totalCount > 0)
                {
                    #region Weak Host Mode
                    if (isLastPackageVersions)
                    {
                        string errorMsg = "Local resources are incomplete. Update required (Please connect to the network)!";
                        Logging.PrintError<Logger>($"GroupName: {groupInfo.groupName}. {errorMsg}");
                        // 當突然失去聯網時, 必須重新從獲取資源版本的流程開始運行, 因為當網絡恢復時, 則可以正確獲取遠端版本進行更新
                        PackageEvents.PatchVersionUpdateFailed.SendEventMessage(this._hashId);
                    }
                    #endregion
                    else
                    {
                        if ((this._machine.Owner as PackageOperation).IsBegin())
                            this._machine.ChangeState<FsmBeginDownload>();

                        /**
                         * 開始等待使用者選擇是否開始下載
                         */
                    }
                }
                else
                {
                    Logging.PrintInfo<Logger>($"GroupName: {groupInfo.groupName} not found any download files!!!");
                    this._machine.ChangeState<FsmDownloadOver>();
                }
            }
        }

        /// <summary>
        /// 6. 下載資源文件
        /// </summary>
        public class FsmBeginDownload : IStateNode
        {
            private StateMachine _machine;
            private int _hashId;

            public FsmBeginDownload() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
                this._hashId = (this._machine.Owner as PackageOperation).hashId;
            }

            void IStateNode.OnEnter()
            {
                // 下載資源文件中
                PackageEvents.PatchFsmState.SendEventMessage(this._hashId, this);
                (this._machine.Owner as PackageOperation).MarkBeginState();
                this._StartDownload().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _StartDownload()
            {
                // Get packages
                var packages = (this._machine.Owner as PackageOperation).GetPackages();

                // Get groupInfo
                GroupInfo groupInfo = (this._machine.Owner as PackageOperation).groupInfo;

                Logging.PrintInfo<Logger>($"Start Download Group Name: {groupInfo?.groupName}, Tags: {JsonConvert.SerializeObject(groupInfo?.tags)}");

                List<ResourceDownloaderOperation> downloaders = new List<ResourceDownloaderOperation>();
                foreach (var package in packages)
                {
                    if (groupInfo != null)
                    {
                        if (groupInfo.tags != null && groupInfo.tags.Length > 0) downloaders.Add(package.CreateResourceDownloader(groupInfo.tags, BundleConfig.maxConcurrencyDownloadCount, BundleConfig.failedRetryCount));
                        else downloaders.Add(package.CreateResourceDownloader(BundleConfig.maxConcurrencyDownloadCount, BundleConfig.failedRetryCount));
                    }
                }

                // Set downloaders
                (this._machine.Owner as PackageOperation).SetDownloaders(downloaders.ToArray());

                // Combine all downloaders count and bytes
                int totalCount = 0;
                long totalBytes = 0;
                foreach (var downloader in downloaders)
                {
                    totalCount += downloader.TotalDownloadCount;
                    totalBytes += downloader.TotalDownloadBytes;
                }

#if !UNITY_WEBGL
                // Check flag if enabled
                if ((this._machine.Owner as PackageOperation).checkDiskSpace)
                {
                    // Check disk space
                    int availableDiskSpaceMegabytes = BundleUtility.CheckAvailableDiskSpaceMegabytes();
                    int patchTotalMegabytes = (int)(totalBytes / (1 << 20));
                    Logging.PrintInfo<Logger>($"[Disk Space Check] Available Disk Space Size: {BundleUtility.GetMegabytesToString(availableDiskSpaceMegabytes)}, Patch Total Size: {BundleUtility.GetBytesToString((ulong)totalBytes)}");
                    if (patchTotalMegabytes > availableDiskSpaceMegabytes)
                    {
                        PackageEvents.PatchCheckDiskNotEnoughSpace.SendEventMessage(availableDiskSpaceMegabytes, (ulong)totalBytes);
                        Logging.PrintError<Logger>($"Disk Not Enough Space!!! Available Disk Space Size: {BundleUtility.GetMegabytesToString(availableDiskSpaceMegabytes)}, Patch Total Size: {BundleUtility.GetBytesToString((ulong)totalBytes)}");
                        return;
                    }
                }
#endif

                // Begin Download
                int currentCount = 0;
                long currentBytes = 0;
                var downloadSpeedCalculator = new DownloadSpeedCalculator();
                downloadSpeedCalculator.onDownloadSpeedProgress = (
                    int totalDownloadCount,
                    int currentDownloadCount,
                    long totalDownloadBytes,
                    long currentDownloadBytes,
                    long downloadSpeedBytes) =>
                {
                    PackageEvents.PatchDownloadProgression.SendEventMessage(
                        this._hashId,
                        totalDownloadCount,
                        currentDownloadCount,
                        totalDownloadBytes,
                        currentDownloadBytes,
                        downloadSpeedBytes);
                };
                foreach (var downloader in downloaders)
                {
                    int lastCount = 0;
                    long lastBytes = 0;
                    downloader.DownloadErrorCallback = (DownloadErrorData data) =>
                    {
                        PackageEvents.PatchDownloadFailed.SendEventMessage(
                            this._hashId,
                            data.FileName,
                            data.ErrorInfo);
                    };
                    downloader.DownloadUpdateCallback = (DownloadUpdateData data) =>
                    {
                        currentCount += data.CurrentDownloadCount - lastCount;
                        lastCount = data.CurrentDownloadCount;
                        currentBytes += data.CurrentDownloadBytes - lastBytes;
                        lastBytes = data.CurrentDownloadBytes;
                        downloadSpeedCalculator.OnDownloadProgress(totalCount, currentCount, totalBytes, currentBytes);
                    };

                    downloader.BeginDownload();
                    await downloader;

                    if (downloader.Status != EOperationStatus.Succeed)
                    {
                        string errorMsg = $"Downloader did not succeed in completing the operation.";
                        Logging.PrintError<Logger>($"{errorMsg}.");
                        return;
                    }
                }

                this._machine.ChangeState<FsmDownloadOver>();
            }
        }

        /// <summary>
        /// 7. 資源下載完成
        /// </summary>
        public class FsmDownloadOver : IStateNode
        {
            private StateMachine _machine;
            private int _hashId;

            public FsmDownloadOver() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
                this._hashId = (this._machine.Owner as PackageOperation).hashId;
            }

            void IStateNode.OnEnter()
            {
                // 資源下載完成
                PackageEvents.PatchFsmState.SendEventMessage(this._hashId, this);
                this._DownloadOver().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _DownloadOver()
            {
                await UniTask.Delay(TimeSpan.FromSeconds(0.1f), true);

                // Get packages
                var packages = (this._machine.Owner as PackageOperation).GetPackages();
                var groupInfo = (this._machine.Owner as PackageOperation).groupInfo;

                int packageTotalCount = 0;
                ulong packageTotalSize = 0;
                for (int i = 0; i < packages.Length; i++)
                {
                    var package = packages[i];
                    packageTotalSize += AssetPatcher.GetPackageSizeInLocal(package.PackageName);
                    packageTotalCount += 1;
                }

                // Set packages total size
                groupInfo.totalCount = packageTotalCount;
                groupInfo.totalBytes = (long)packageTotalSize;

                this._machine.ChangeState<FsmClearCache>();
            }
        }

        /// <summary>
        /// 8. 清理未使用的緩存文件
        /// </summary>
        public class FsmClearCache : IStateNode
        {
            private StateMachine _machine;
            private int _hashId;

            public FsmClearCache() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
                this._hashId = (this._machine.Owner as PackageOperation).hashId;
            }

            void IStateNode.OnEnter()
            {
                // 清理未使用的緩存文件
                PackageEvents.PatchFsmState.SendEventMessage(this._hashId, this);
                this._ClearUnusedCache().Forget();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }

            private async UniTask _ClearUnusedCache()
            {
                await UniTask.Delay(TimeSpan.FromSeconds(0.1f), true);

                // Get packages
                var packages = (this._machine.Owner as PackageOperation).GetPackages();

                foreach (var package in packages)
                {
                    var clearUnusedBundleFilesOperation = package.ClearCacheFilesAsync(EFileClearMode.ClearUnusedBundleFiles);
                    var clearUnusedManifestFilesOperation = package.ClearCacheFilesAsync(EFileClearMode.ClearUnusedManifestFiles);
                    await clearUnusedBundleFilesOperation;
                    await clearUnusedManifestFilesOperation;
                }

                this._machine.ChangeState<FsmPatchDone>();
            }
        }

        /// <summary>
        /// 9. 更新完畢
        /// </summary>
        public class FsmPatchDone : IStateNode
        {
            private StateMachine _machine;
            private int _hashId;

            public FsmPatchDone() { }

            void IStateNode.OnCreate(StateMachine machine)
            {
                this._machine = machine;
                this._hashId = (this._machine.Owner as PackageOperation).hashId;
            }

            void IStateNode.OnEnter()
            {
                // 更新完畢
                PackageEvents.PatchFsmState.SendEventMessage(this._hashId, this);
                (this._machine.Owner as PackageOperation).MarkPatchAsDone();
            }

            void IStateNode.OnUpdate()
            {
            }

            void IStateNode.OnExit()
            {
            }
        }
    }
}
