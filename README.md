# KeyLet

一款专为 Windows 设计的**极简、绿色便携、单文件、低占用**本地 2FA（TOTP）桌面管理工具。

> [!NOTE]
> 当前仓库处于初始化阶段，源码将在 `src/` 目录下逐步完善。

## 核心特性（规划）

- **绿色单文件便携**：发布为单个 `KeyLet.exe`，无需安装；所有运行数据严格保存在同级 `data/` 目录，关闭即彻底退出，零后台进程。
- **外部可读加密存储**：使用初始主密码通过 `PBKDF2-SHA256` 派生密钥，并以 `AES-256-GCM` 加密敏感字段存入 `data/vault.csv`；即使不打开软件，也可凭主密码通过外部脚本独立解密读取。
- **本机免密秒开**：支持利用 Windows DPAPI 将解锁密钥受保护地缓存于本机 `data/` 中，打开软件无需每次输入密码。
- **隐藏前后缀与灵活复制**：支持为动态码配置隐藏前缀与后缀（如 `qwer123456tyu`），主界面保持极简隐藏，复制时自由选择“纯动态码”或“带前后缀完整码”。
- **多方式密钥导入**：支持 Base32 字符串、标准 `otpauth://` URI、二维码图片、剪贴板截图（`Ctrl+V`）与屏幕区域扫码。

## 目录结构

```text
KeyLet/
├── README.md        # 项目说明与使用指南
├── .gitignore       # Git 忽略规则（严格屏蔽个人 data/ 密钥与构建产物）
├── build.ps1        # 构建、测试与单文件发布脚本
├── dist/            # 单文件发布输出目录（Git 忽略）
└── src/             # C# / .NET 10 源代码目录
    ├── KeyLet.Core/ # 核心业务、加解密、TOTP 与 CSV 存储
    ├── KeyLet.App/  # WPF 桌面端界面与平台交互
    └── KeyLet.Tests/# 单元测试
```

## 构建与发布

需要 **.NET 10 SDK**（Windows）。在 `KeyLet/` 目录下执行：

```powershell
# 运行测试与 Debug 构建
.\build.ps1 -Task Test

# 发布独立单文件 KeyLet.exe 到 dist/ 目录
.\build.ps1 -Task Publish
```
