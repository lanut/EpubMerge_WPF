# Microsoft Store 首发与跨电脑配置指南

## 两个独立分发通道

| 渠道 | 内容 | 入口 |
|---|---|---|
| GitHub Release | 非打包 ZIP，包含 WPF、WinUI、CLI；各有独立目录 | [build-release.yml](../.github/workflows/build-release.yml)、[publish.ps1](../publish.ps1) |
| Microsoft Store | 只有 WinUI 的 MSIX；本阶段构建并保存 Actions 工件，人工上传 Partner Center | [build-store.yml](../.github/workflows/build-store.yml)、[publish-store.ps1](../publish-store.ps1) |

不把未签名 MSIX 加到公开 GitHub Release，不从商店导出包作为 GitHub 安装包。本阶段不接自动提交商店，不需要 Entra Client Secret、Seller ID 或签名证书 Secrets，也不启用 AOT/裁剪。

商店模式使用 **.NET 自包含 + Windows App SDK 框架依赖**：

- MSIX 内有 .NET 10 CoreCLR，用户不需另外安装 .NET。
- Windows App SDK 框架依赖由官方构建目标生成；商店负责依赖部署。不能把普通 .NET 10 Runtime 当作 UWP .NET Native 框架包。
- 本地侧载不等于商店安装；测试时还要部署对应框架依赖。
- 原 GitHub ZIP 的自包含/依赖框架选择不受影响。

## 1. 在另一台电脑继续工作

先拉取包含此功能的分支/提交。只在 GitHub 上构建时，不需要在本机安装开发工具或 ARM 编译工具。

如果要本地构建：

1. Windows 11，稳定版 .NET 10 SDK；仓库 [global.json](../global.json) 排除预览 SDK。
2. Visual Studio 的 WinUI 应用开发工具和 MSBuild。
3. Windows SDK 26100（当前项目 BuildTools 版本为 `10.0.26100.9457`），包含 MakeAppx 和 SignTool。
4. 使用 PowerShell 7，仓库根目录为当前目录。x64 本地验证足够，ARM64 交给 GitHub Actions。

## 2. 尚未创建产品：先做演练

```powershell
.\tests\Packaging\StorePackaging.Tests.ps1
.\publish-store.ps1 -PackageVersion 1.2.3.0 -RuntimeIdentifier win-x64 -Demo
```

输出：`publish\msix\demo\1.2.3.0\win-x64\`。包括 `.msixupload`、测试 MSIX/依赖目录和 `package-validation.json`。

演练身份为 `EpubMerge.WinUI.Demo`，显示名称包含 DEMO。**它不是正式商店包，不要提交 Partner Center。** 演练开关必须显式提供；缺少真实身份时不会自动切到演练。

也可在 GitHub 的 Actions 中选择 **Build Microsoft Store packages → Run workflow**：

- 选择包含这些文件的分支。
- `package_version` 填 `1.2.3.0`。
- 保持 `demo=true`。
- 下载两个架构的 `msix-DEMO-NOT-FOR-STORE-...` 工件。Actions 外层下载 ZIP 只是工件传输容器。

## 3. 创建正式 Store 产品并填写身份

1. 在 [Partner Center](https://partner.microsoft.com/dashboard) 完成 Windows 应用开发者账号注册。
2. 创建应用并保留产品名称；选择 MSIX 应用提交路径。
3. 在该应用的 **Product management / Product identity** 页面找到包身份信息。页面名称可能随后台更新变化，以显示的字段为准。
4. 修改并保存 [packaging/store-settings.json](../packaging/store-settings.json)：

| 配置字段 | 来源 |
|---|---|
| `IdentityName` | Package/Identity/Name，完整复制，不是可读应用名称 |
| `Publisher` | Package/Identity/Publisher，包含 `CN=`，完整复制，不能自己拼 |
| `PublisherDisplayName` | Package/Properties/PublisherDisplayName，按后台要求填写 |
| `DisplayName` | 希望显示的应用名称，与保留的名称及商店规则一致 |
| `Description` | 简短包描述；商店长描述在 Partner Center 单独填写 |

**Product ID、Seller ID、GitHub 用户名均不等于包 Identity Name 或 Publisher。** 这些包身份字段不是密钥，可以随代码提交；pfx、私钥和 Client Secret 不能提交。

初始配置刻意把前三项留空。正式构建发现缺失/占位/演练身份会失败。脚本只能校验格式，无法代替 Partner Center 核对真实归属。

在另一台电脑将配置提交并推送到需要构建的分支；否则远端仍使用旧的空配置。

## 4. 版本与正式构建

包版本为 `Major.Minor.Build.0`，每段不超过 65535，Major 大于 0，revision 固定为 0，不允许前导零。每次更新必须高于已上传的版本；脚本不会查询商店历史。

本地：

```powershell
.\publish-store.ps1 -PackageVersion 1.2.3.0 -RuntimeIdentifier win-x64
```

GitHub 手动构建：

1. Run workflow，填写版本。
2. **取消 demo**。
3. 两个架构任务完成后下载 `msix-STORE-win-x64-...`、`msix-STORE-win-arm64-...`。

标签自动构建：

```powershell
git tag V1.2.3
git push origin V1.2.3
```

- Store workflow 将 `V1.2.3` 转成 `1.2.3.0`，始终正式模式。
- Store 标签只接受 `VMajor.Minor.Build`，预发布标签/四段标签需使用手动触发并提供合法包版本。
- **尚未配置身份时，V 标签的 Store workflow 失败是预期行为；独立 ZIP Release workflow 不依赖它，仍可正常发布。**
- 构建不会自动上传 Partner Center。Actions 工件保存 14 天，及时下载归档。

正式输出：`publish\msix\store\<版本>\<架构>\`。优先上传两个架构各自的 `.msixupload`，在 Partner Center 同一个版本提交中同时添加，不需要为每个架构创建一个商店产品。

`package-validation.json` 记录已核对的身份、版本、架构、签名状态和 Windows App SDK 框架依赖。不要把演练与正式版本混用。

## 5. 人工首发清单

在 Partner Center 完成：

- 产品名称、价格和可用性、市场选择。
- 中英文商店简介、功能说明、真实截图、支持联系方式。
- 隐私政策 URL：根据真实行为撰写，说明本地 EPUB 处理与存储；不要声称收集/不收集与实际不符的数据。
- 年龄分级、产品声明、所需硬件和系统要求。
- 包上传和兼容性校验，确认 x64 / ARM64、身份与版本正确。
- `runFullTrust` 为桌面 WinUI 所需的完整信任声明。权限理由应说明用户主动选择本地 EPUB/封面、合并后写入用户指定位置，不申请与功能无关的权限。
- 为认证人员提供操作步骤、可合法分发的测试 EPUB、预期输出和无账号登录要求的说明（若实际如此）。
- 决定认证后立即发布还是手动发布，提交认证并查看结果。构建成功不等于通过商店认证。

本次本地验证记录（2026-11-10）：

- x64 DEMO `1.1.3.0` 成功生成 `.msixupload`，解开其中实际提交的 MSIX，校验身份、版本、PE 架构、PRI、语言/图标资源、内置 CoreCLR 和自包含 runtimeconfig。
- 清单生成的框架依赖为 `Microsoft.WindowsAppRuntime.2 >= 2.6.5.0`，不是手工猜测的 .NET 依赖。
- 9 个配置/版本失败场景通过；默认 solution 的 Debug/Release 构建零警告、零错误，68 项 .NET 测试通过。首次全套测试遇到一次既有历史文件锁异常，针对性及全套重跑通过，未改动历史存储逻辑。
- 原 x64 两种 ZIP 部署目录均重新发布，四个 GUI 入口启动响应，CLI 帮助正常；原 ZIP workflow 和脚本未改动。
- 用户选择不进行签名安装验证，因此没有创建证书、改动信任存储或安装演练包。**有包身份后的实际启动、文件选择器、历史/主题、合并行为尚需在测试环境验证。**
- 真实身份验证、远端 workflow、ARM64 原生运行、干净系统依赖部署和商店认证仍需要实际执行。

## 6. 本地安装测试：需要测试签名

商店提交包是**未签名**的，由 Store 在提交处理时签名。`.msixupload` 不是可双击安装的安装程序。未签名 `.msix` 也不能直接双击安装。

建议在测试虚拟机中进行；不要为了测试在生产电脑导入不明证书。演练包目录 `_Test` 中的 Dependencies 含 SDK 生成的框架依赖，请安装匹配架构的依赖，再安装签名后的主包。

以下为可选测试流程，仅针对 DEMO 身份。按微软签名指南，在**测试虚拟机的管理员 PowerShell** 中运行：私钥留在当前用户证书存储，仅公钥导入本机 `TrustedPeople`，结束按指纹移除。未经许可不要修改机器信任存储。

```powershell
# 在仓库根目录运行；请先定位本次生成的主 MSIX。
$package = Get-ChildItem 'publish\msix\demo\1.2.3.0\win-x64' -Recurse -Filter '*.msix' |
    Where-Object { $_.Name -notlike 'Microsoft.*' } |
    Select-Object -First 1
$signedCopy = Join-Path $env:TEMP 'EpubMerge-Demo-Signed.msix'
Copy-Item -LiteralPath $package.FullName -Destination $signedCopy

$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=EpubMerge.WinUI.Demo' `
    -KeyUsage DigitalSignature -FriendlyName 'EpubMerge demo test only' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')

$cer = Join-Path $env:TEMP 'EpubMerge-Demo-Test.cer'
Export-Certificate -Cert $cert -FilePath $cer
Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'

$signTool = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
& $signTool sign /fd SHA256 /s My /sha1 $cert.Thumbprint $signedCopy
if ($LASTEXITCODE -ne 0) { throw 'Test signing failed.' }
& $signTool verify /pa /v $signedCopy
if ($LASTEXITCODE -ne 0) { throw 'Test signature verification failed.' }

# 先用 Add-AppxPackage 安装 _Test\Dependencies 中匹配架构且尚未安装的框架包。
# 缺少依赖时不要忽略错误；按 package-validation.json 和系统错误定位。
Add-AppxPackage -Path $signedCopy
```

仅针对自己生成的演练包，测试启动、中英文、主题、选择/取消、导入、合并、历史恢复、输出路径以及日志。MSIX 安装目录只读，不应写入；包身份可能引起文件系统/注册表重定向，不保证与 ZIP 共享历史或主题。当前代码不强制搬迁已有用户数据。

结束后关闭测试应用，按本次记录的确切包名及证书指纹清理：

```powershell
Get-AppxPackage -Name 'EpubMerge.WinUI.Demo' | Remove-AppxPackage
Remove-Item -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)"
Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)"
Remove-Item -LiteralPath $signedCopy, $cer
```

不要删除其他证书或共享框架依赖。正式包本地测试时必须使用与正式 manifest Publisher 匹配的证书，且不要覆盖正在使用的正式安装。

## 7. 故障排查与后续自动化

- **缺少身份**：按第 3 节配置；仅演练时提供 `-Demo`，不应为通过 CI 随便填假身份。
- **找不到 MSBuild/MakeAppx**：安装上述 VS/SDK 组件；普通 `dotnet publish` 的 ZIP 不能替代商店打包。
- **依赖缺失**：看校验报告中的依赖及系统错误；不要移除 manifest 依赖来掩盖问题。
- **证书不受信任/Publisher 不符**：核对测试证书 Subject、包 Publisher 和信任范围，不通过关闭系统安全机制绕过。
- **版本已使用**：提高前三段版本，revision 保持 0；两个架构用同一版本。
- **认证拒绝权限/隐私/素材**：按实际业务与 Partner Center 的反馈修改；本地构建检查不代表认证承诺。

首发上线后，可另行接入 Microsoft Store CLI、Entra 应用授权及受保护的 GitHub Environment，实现审批后自动提交更新；不属于本次实现。

官方参考：

- [单项目 MSIX](https://learn.microsoft.com/windows/apps/windows-app-sdk/single-project-msix)
- [WinUI CI 与 StoreUpload](https://learn.microsoft.com/windows/apps/package-and-deploy/ci-for-winui3)
- [框架依赖打包应用](https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-packaged-apps)
- [Windows App SDK 部署架构](https://learn.microsoft.com/windows/apps/windows-app-sdk/deployment-architecture)
- [MSIX 签名指南](https://learn.microsoft.com/windows/msix/package/sign-msix-package-guide)
- [后续 Store 自动更新](https://learn.microsoft.com/windows/apps/publish/msstore-dev-cli/github-actions)
