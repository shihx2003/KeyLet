<p align="center">
  <img src="src/KeyLet.App/Resources/KeyLetLogo.png" width="96" height="96" alt="KeyLet Logo" />
</p>

<h1 align="center">KeyLet</h1>

<p align="center">
  Windows 绿色便携单文件本地 2FA（TOTP）桌面管理工具 · 基于 .NET 10 + WPF Fluent UI
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D4?style=flat-square" alt="Windows" />
  <img src="https://img.shields.io/badge/License-MIT-10B981?style=flat-square" alt="MIT License" />
</p>

---

## 核心特性

- **绿色单文件 · 零后台**：发布为单一 `KeyLet.exe`，无需安装；所有运行数据严格保存在同级 `data/` 目录，关闭窗口即清理内存密钥并彻底退出。
- **App 不打开也能独立读取**：采用 `PBKDF2-SHA256` + `AES-256-GCM` 加密敏感列并存入标准 `data/vault.csv`，账号标识与算法参数明文保留；任何时候均可使用主密码通过外部脚本独立解密读取。
- **本机免密打开**：结合 Windows **DPAPI**（`CurrentUser`）在 `data/local.unlock` 缓存解锁密钥，本机打开直达主界面；跨设备迁移时输入主密码即可重新解锁。
- **隐藏动态码前后缀 + 可选复制**：支持为动态码配置隐藏前缀与后缀（如实际复制 `qwer123456tyu`），主界面隐藏明文仅展示 `前后缀` 徽标，支持点击默认复制、`Shift+点击` 切换、按钮与右键自由选择“纯码 / 完整码”，并支持复制后自动安全清空剪贴板。
- **多种密钥导入方式**：支持 Base32 密钥、`otpauth://totp/...` URI、Google Authenticator 批量迁移（`otpauth-migration://`）、选择或拖拽二维码图片、`Ctrl+V` 粘贴剪贴板截图以及屏幕框选扫码。

---

## 快捷操作一览

| 操作 | 效果 |
| :--- | :--- |
| **左键点击账号卡片** | 按该账号默认复制模式复制，并定时自动清空剪贴板 |
| **`Shift + 左键点击卡片`** | 临时切换复制模式（纯码 ↔ 完整码） |
| **卡片按钮 / 右键菜单** | 显式选择复制 `纯码`、`完整码` 或编辑/删除账号 |
| **`Ctrl + V`（主界面 / 添加窗）** | 直接识别剪贴板中的二维码截图或图片文件 |
| **拖入图片文件** | 拖入窗口即自动识别二维码并导入 |
| **扫码按钮** | 框选屏幕任意区域截图并自动识别二维码 |

---

## 目录结构

```text
KeyLet/
├── README.md          # 项目说明、加密规范与外部解密指南
├── LICENSE            # MIT 开源协议
├── .gitignore         # 屏蔽个人 data/ 密钥文件与构建输出
├── build.ps1          # 一键构建、测试与单文件发布脚本
├── KeyLet.sln         # .NET 10 解决方案
└── src/
    ├── KeyLet.Core/   # 核心模型、PBKDF2/AES-GCM 加解密、TOTP、CSV 仓储与 URI 解析（无 UI 依赖）
    ├── KeyLet.App/    # WPF Fluent 桌面应用（DPAPI、剪贴板、屏幕/图片二维码识别）
    └── KeyLet.Tests/  # xUnit 自动化单元测试
```

运行后在 `KeyLet.exe` 同级自动维护 `data/` 目录：

```text
KeyLet.exe
└── data/
    ├── vault.meta.json  # KDF 算法、随机 Salt、迭代次数与主密码校验令牌
    ├── vault.csv        # 账号列表（元信息明文，Secret/Prefix/Suffix 为 AES-256-GCM 密文）
    ├── settings.json    # 主题、剪贴板自动清理秒数、默认复制模式等配置
    ├── local.unlock     # 可选：本机 DPAPI 免密解锁缓存
    └── logs/            # 运行诊断日志（不记录任何明文密钥）
```

---

## 构建与单文件发布

环境要求：Windows + **.NET 10 SDK**。

```powershell
# 运行单元测试
.\build.ps1 -Task Test

# 编译解决方案
.\build.ps1 -Task Build

# 发布独立单文件 KeyLet.exe 到 dist/（内置运行时，可直接在任意 Win10/11 电脑运行）
.\build.ps1 -Task Publish -Runtime win-x64 -SelfContained $true

# 或发布依赖系统 .NET 10 桌面运行时的超小单文件版
.\build.ps1 -Task Publish -Runtime win-x64 -SelfContained $false
```

---

## 加密规范与外部独立解密

### 1. 加密格式说明
1. **密钥派生（KDF）**：读取 `data/vault.meta.json` 中的 `SaltBase64`（16 字节随机盐）和 `Iterations`（默认 `100000`），通过 `PBKDF2-SHA256` 从主密码（UTF-8）派生 32 字节对称密钥。
2. **字段密文格式**：`vault.csv` 的 `EncryptedSecret`、`EncryptedPrefix`、`EncryptedSuffix` 及 `vault.meta.json` 的 `KeyCheckToken` 均使用 `AES-256-GCM` 加密，格式为：
   ```text
   v1:<Base64(12B Nonce)>:<Base64(Ciphertext)>:<Base64(16B Tag)>
   ```

### 2. Python 独立读取示例（无需打开 KeyLet）

安装依赖：`pip install cryptography`

```python
import base64
import csv
import getpass
import json
from pathlib import Path
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.pbkdf2 import PBKDF2HMAC


def decrypt_v1(payload: str, key: bytes) -> str:
    if not payload:
        return ""
    version, nonce_b64, cipher_b64, tag_b64 = payload.split(":", 3)
    if version != "v1":
        raise ValueError(f"Unsupported payload version: {version}")
    nonce = base64.b64decode(nonce_b64)
    ciphertext = base64.b64decode(cipher_b64)
    tag = base64.b64decode(tag_b64)
    return AESGCM(key).decrypt(nonce, ciphertext + tag, None).decode("utf-8")


def main() -> None:
    data_dir = Path("data")
    meta = json.loads((data_dir / "vault.meta.json").read_text(encoding="utf-8"))
    password = getpass.getpass("请输入 KeyLet 主密码: ")

    kdf = PBKDF2HMAC(
        algorithm=hashes.SHA256(),
        length=32,
        salt=base64.b64decode(meta["SaltBase64"]),
        iterations=int(meta["Iterations"]),
    )
    key = kdf.derive(password.encode("utf-8"))

    if decrypt_v1(meta["KeyCheckToken"], key) != "KEYLET-VAULT-V1":
        raise ValueError("主密码错误")

    with (data_dir / "vault.csv").open("r", encoding="utf-8", newline="") as f:
        for row in csv.DictReader(f):
            secret = decrypt_v1(row["EncryptedSecret"], key)
            prefix = decrypt_v1(row["EncryptedPrefix"], key)
            suffix = decrypt_v1(row["EncryptedSuffix"], key)
            print(
                f"[{row['Issuer']}] {row['AccountName']} | "
                f"Secret={secret} | Prefix={prefix!r} | Suffix={suffix!r}"
            )


if __name__ == "__main__":
    main()
```

---

## 开源协议

本项目基于 [MIT License](LICENSE) 开源。
