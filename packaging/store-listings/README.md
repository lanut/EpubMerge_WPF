# Microsoft Store 多语言文案

这些文件是供 Partner Center 手动填写的 WinUI 商店页面文案，不是自动上传文件，也不是应用界面资源。每个语言文件包含建议标题、简短描述、完整描述和功能列表；复制字段正文，不要复制 Markdown 标题。简短描述可用作简短宣传语，是否有独立字段以当前后台为准。完整描述采用纯文本段落，功能列表可逐项填入产品功能字段。

## 身份与显示名称

- 计划使用的 Package/Identity/Name：`lanut.EpubMerge`，已写入 [store-settings.json](../store-settings.json)，**尚待 Partner Center 核对**。
- 它不是商店 Product ID、Package Family Name 或商店显示标题，不要相互替代。
- `Publisher` 和 `PublisherDisplayName` 仍留空；从该产品的 Product identity 页面逐字复制，不要自行推导 GUID 或使用演练证书名称。
- 当前包显示名称仍为 `EPUB Merger`。下方各语言标题是建议文案，并非已经保留的名称。先在 Partner Center 保留要用的名称，再在各语言页面选择；若仅保留 `EPUB Merger`，各语言页面均使用该名称，将本地化用途说明留在描述中。
- 确认最终显示名后，再同步包配置的 `DisplayName`。设置目录名或修改清单不会替你注册商店产品。

## 文案目录

| 语言 | 页面 |
|---|---|
| 简体中文 | [zh-CN.md](zh-CN.md) |
| English | [en-US.md](en-US.md) |
| 繁體中文 | [zh-TW.md](zh-TW.md) |
| 日本語 | [ja-JP.md](ja-JP.md) |
| 한국어 | [ko-KR.md](ko-KR.md) |
| Français | [fr-FR.md](fr-FR.md) |
| Deutsch | [de-DE.md](de-DE.md) |
| Español | [es-ES.md](es-ES.md) |
| Português (Brasil) | [pt-BR.md](pt-BR.md) |
| Русский | [ru-RU.md](ru-RU.md) |

## 提交前

1. 在 Partner Center 添加对应的十种商店页面语言；它们与应用当前支持的语言一致。
2. 审校各语言，尤其是非母语译文。本文案不承诺完美兼容、通过认证或特定阅读器效果，也不宣传尚未完成的 ARM64 实机验收和 AOT 商店安装。
3. 每个页面补充实际应用截图及对应说明。当前目录仅包含文字，不构成完整上架资料；按后台要求完成分类、年龄分级、支持和隐私信息等项目。
4. 仅宣传 WinUI 商店版，不把 GitHub WPF/CLI 功能、性能或分发方式当作商店承诺。本文案未宣称支持 DRM、PDF 转换或云同步。
5. 标题控制在 50 字符内，简短描述控制在 200 字符内，完整描述控制在 10,000 字符内；最终限制及必填项以后台为准。
6. 使用实际 MSIX 验收通过的版本。签名安装与 ARM64 验收仍按 [商店指南](../../docs/MICROSOFT-STORE.md) 执行。

如需批量导入，先从 Partner Center 导出该产品的 CSV，再依据其字段和语言列填入本文案；这些 Markdown 文件不能直接当作导入 CSV。

参考：[MSIX 商店页面字段与多语言配置](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info)。
