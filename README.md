# KeyLet

一款专为 Windows 打造的**绿色便携、单文件、低占用、外部可读加密存储**本地 2FA（TOTP）桌面管理应用，基于 **.NET 10 + WPF + Fluent UI** 构建。

---

## 核心特性

- **真正绿色单文件**：发布为单一 `KeyLet.exe`，无需安装；所有运行数据严格保存在同级 `data/` 目录，关闭窗口立即释放内存密钥并退出进程，**零后台常驻**。
- **App 不打开也能独立读取**：采用 `PBKDF2-SHA256` + `AES-256-GCM` 加密核心字段并存入标准 `data/vault.csv`（账号名称与算法参数保持明文列），附带完整加密规范与 Python 解密脚本，任何时候输入主密码即可脱离本软件读取全部密钥。
- **本机免密秒开**：支持 Windows **DPAPI**（`CurrentUser`）将派生密钥安全缓存在 `data/local.unlock`，本机打开软件无需每次输入密码；跨设备拷贝时输入主密码即可重新解锁。
- **隐藏动态码前后缀 + 可选复制**：
  - 支持为动态码配置隐藏前缀与后缀（如实际复制 `qwer123456tyu`），主界面默认隐藏前后缀明文，仅展示 `已配前后缀` 徽标；
  - 支持 **左键点击卡片按默认模式复制**、**`Shift+点击` 切换复制模式**、**卡片快捷按钮** 与 **右键菜单** 自由选择“纯动态码”或“带前后缀完整码”；
  - 支持复制后倒计时（默认 20 秒）自动核对并清空剪贴板。
- **多途径密钥导入**：
  - 手动输入 Base32 密钥或粘贴 `otpauth://totp/...` URI；
  - 支持 Google Authenticator 批量迁移协议 `otpauth-migration://offline?data=...`；
  - 选择二维码图片 / 拖拽图片入窗识别；
  - `Ctrl+V` 直接粘贴剪贴板中的二维码截图；
  - 一键“屏幕框选扫码”，直接框选屏幕任意位置的二维码导入。

---

## 目录结构

```text
KeyLet/
├── README.md        # 项目说明、加密规范与外部解密指南
├── LICENSE          # MIT 开源协议
├── .gitignore       # 屏蔽个人 data/ 密钥文件与构建输出
├── build.ps1        # 一键构建、测试与单文件发布脚本
├── KeyLet.sln       # .NET 10 解决方案
└── src/
    ├── KeyLet.Core/ # 核心模型、PBKDF2/AES-GCM 加解密、TOTP、CSV 仓储与 URI 解析（无 UI 依赖）
    ├── KeyLet.App/  # WPF Fluent 桌面应用（DPAPI、剪贴板、屏幕/图片二维码识别）
    └── KeyLet.Tests/# xUnit 自动化单元测试
```

发布运行后将在 `KeyLet.exe` 同级自动生成 `data/` 目录：

```text
KeyLet.exe
└── data/
    ├── vault.meta.json  # KDF 算法、随机 Salt、迭代次数与主密码校验令牌
    ├── vault.csv        # 账号列表（元信息明文，Secret/Prefix/Suffix 为 AES-256-GCM 密文）
    ├── settings.json    # 主题、剪贴板自动清理秒数、默认复制模式等配置
    └── local.unlock     # 可选：启用“本机免密打开”时由 Windows DPAPI 保护的密钥缓存
```

---

## 构建与单文件发布

环境要求：Windows + **.NET 10 SDK**。

```powershell
# 运行全部单元测试
.\build.ps1 -Task Test

# 编译解决方案
.\build.ps1 -Task Build

# 发布独立单文件 KeyLet.exe 到 dist/ 目录（不依赖目标机安装 .NET 运行时）
.\build.ps1 -Task Publish -Runtime win-x64 -SelfContained $true
```

---

## 加密规范与“不打开 App 外部读取”指南

### 1. 密钥派生与密文格式
1. **密钥派生（KDF）**：读取 `data/vault.meta.json` 中的 `SaltBase64`（Base64 解码为 16 字节）和 `Iterations`（默认 `100000`），使用 `PBKDF2-SHA256` 从用户主密码（UTF-8）派生 32 字节（256 位）对称密钥。
2. **字段加密**：`vault.csv` 中的 `EncryptedSecret`、`EncryptedPrefix`、`EncryptedSuffix` 与 `vault.meta.json` 中的 `KeyCheckToken` 均使用 `AES-256-GCM` 加密，格式统一为：
   ```text
   v1:<Base64(12B Nonce)>:<Base64(Ciphertext)>:<Base64(16B Tag)>
   ```

### 2. Python 外部独立解密示例（无需打开 KeyLet）

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

    # 验证主密码是否正确
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
