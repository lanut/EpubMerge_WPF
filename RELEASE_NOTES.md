## 发布说明

`SelfContained` 不需要下载 `.NET 10` 环境

`FrameworkDependent` 需要下载 `.NET 10` 环境即可运行

## 当前更新

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

- GUI 和 CLI 现在发布到同一个架构/部署模式目录，便于共同分发和后续共享配置：

	```text
	publish/<rid>/<self-contained|framework-dependent>/
		EpubMerge.Gui.exe
		EpubMerge.Cli.exe
	```

- 发布流程新增 CLI `--help` smoke test。
- 新增 CLI 测试项目，覆盖 `--quiet`、plain、JSONL、重定向输出和 ANSI 控制字符行为。
