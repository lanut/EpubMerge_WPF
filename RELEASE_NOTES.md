## 发布说明

`SelfContained` 不需要下载 `.NET 10` 环境

`FrameworkDependent`：WPF 需要对应架构的 `.NET 10 Desktop Runtime`；CLI 需要 `.NET 10 Runtime`。GitHub ZIP 不再包含 WinUI。

## 当前更新

- WinUI 页脚补齐 WPF 的作者信息与 GitHub 矢量图标，使用 WinUI `Path` 随主题调整颜色；新增英文 Disclaimer 弹窗入口及对应英文说明，不改变 WPF。

### WinUI 多语言与语言菜单

- 支持简中、英语、繁中、日语、韩语、法语、德语、西班牙语、巴西葡萄牙语和俄语，共 10 种界面语言。
- 原语言 ComboBox 改为显示当前语言的下拉按钮，菜单使用语言自称并标记选中项，支持即时切换。
- 首次启动跟随系统 UI 语言，不支持则回退英语；记住用户主动选择，不改变 WPF/CLI 和 EPUB 内容。
- 补齐所有资源键及格式参数测试，同步 MSIX 语言声明与 Managed 卫星资源检查；保留 Native AOT 兼容和 Managed 商店默认。
- 改善长翻译在小窗口中的按钮换行；商店构建使用 Rebuild，避免切换版本后复用旧清单。

### 商店 MSIX 与 GitHub ZIP 双通道

- WinUI 新增显式单项目 MSIX 构建；默认仍为非打包应用，WPF/CLI 发布行为不变。
- 商店包内置 .NET，通过框架依赖部署 Windows App SDK；包身份由独立配置生成，正式模式拒绝缺失/占位身份，演练模式须显式选择。
- x64/ARM64 MSIX Workflow 仅手动触发，显式填写版本，仅保存 Actions 工件，人工上传商店，不向 GitHub Release 附加 MSIX。
- GitHub Release 仅包含 WPF/CLI，保留 V 标签触发、x64/ARM64 及自包含/依赖框架四种 ZIP；不触发商店构建。
- 新增 [跨电脑配置与人工首发指南](docs/MICROSOFT-STORE.md)，包含创建产品、填写身份、版本、签名测试和包上传步骤。
- WinUI 新增显式 `NativeAot` 编译试验，修复动态本地化为编译绑定；x64 原生发布与实际 MSIX 包体检查通过。包内无 CoreCLR，Windows App SDK 仍由商店以框架依赖部署。
- 商店默认保留 `Managed`，不静默回退；有包身份的安装运行由用户在隔离环境验收，ARM64 构建/设备验收尚待完成。WPF/CLI 不启用 AOT，自动商店提交仍未启用。

### WPF 与 WinUI 3 并存

- WinUI 合并完成弹窗新增“关闭”按钮；关闭或按 Esc 仅关闭弹窗，不再打开输出目录。
- 保留原版 WPF `EpubMerge.Gui`，新增独立非打包 WinUI 3 项目 `EpubMerge.Gui.WinUI`，基于 .NET 10 与 Windows App SDK 2.6.5。
- 保留文件拖放与排序、封面、历史任务、进度取消、实时中英文及系统/浅色/深色主题。
- WinUI 使用独立 Presentation 层及测试项目；WPF 保留原有 ViewModel、资源和测试，不引用 Presentation。
- WinUI 历史与日志保留在 `%LOCALAPPDATA%\EpubMerge`，JSON 使用源生成；主题使用独立注册表键，不自动导入 WPF 历史。WPF 存储行为不变。
- ZIP 中仅使用独立 `wpf`、`cli` 子目录；WPF 和 CLI 保留单文件托管程序集打包。
- ZIP 自包含包携带 .NET/Windows Desktop 运行时；WinUI 仅通过商店 MSIX 分发，非打包模式仅用于本地开发与诊断。

### 新增独立 CLI

- 新增独立的 `EpubMerge.Cli` 控制台项目，不再依赖 WPF GUI。
- 移除 GUI 的 `--cli` / `-c` 内嵌命令行入口，因为 winexe 不支持在原控制台输入输出。
- 保留原有参数处理、错误分类和退出码约定：`0` 成功、`1` 参数或输入错误、`2` 取消或运行期失败。

### CLI 进度输出

- 默认使用 `Spectre.Console` 显示动态进度条。
- 新增 `--progress plain`，输出适合管道读取的稳定文本行。
- 新增 `--progress jsonl`，每行输出一个 JSON 进度事件。
- 默认输出被重定向时自动降级为普通文本行，避免 ANSI 控制字符干扰其他程序读取。
- `--quiet` 不输出进度和成功摘要，错误仍写入标准错误流。
- JSONL 使用 `System.Text.Json` Source Generator 进行强类型序列化。

### 进度契约

- Core 进度模型新增结构化阶段：`Reading` 和 `Merging`。
- GUI 不再通过解析本地化文本判断当前进度阶段。

### 源 EPUB 读取稳定性修复

- 修复源 EPUB 在复制或下载尚未完成时读取，导致 `End of Central Directory record could not be found` 的问题。
- 打开源 EPUB 时增加最多 5 次、每次间隔 250 毫秒的可取消重试，降低临时文件不完整造成的失败。
- 对仍然损坏或截断的 EPUB，错误信息现在包含具体文件路径，并明确提示文件可能仍在写入或已损坏。
- CLI 进度报告改为同步派发，避免异常发生时显示的最后一个读取序号滞后于实际失败文件。

### 发布与测试

- WPF 和 CLI 发布到独立子目录，避免运行时依赖互相覆盖：

	```text
	publish/<rid>/<self-contained|framework-dependent>/
		wpf/EpubMerge.Gui.exe
		cli/EpubMerge.Cli.exe
	```

- 发布流程新增 CLI `--help` smoke test。
- 新增 CLI 测试项目，覆盖 `--quiet`、plain、JSONL、重定向输出和 ANSI 控制字符行为。
