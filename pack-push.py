#!/usr/bin/env python3
"""BitSerializer 打包与推送脚本"""

import subprocess
import sys
import os
import re
import getpass
import glob
import shutil

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
GENERATOR_PROJ = os.path.join(SCRIPT_DIR, "BitSerializer.Generator", "BitSerializer.Generator.csproj")
MAIN_PROJ      = os.path.join(SCRIPT_DIR, "BitSerializer", "BitSerializer.csproj")
OUTPUT_DIR     = os.path.join(SCRIPT_DIR, "nupkg")

# ── 颜色 ──────────────────────────────────────────────────────
class C:
    RED    = "\033[0;31m"
    GREEN  = "\033[0;32m"
    YELLOW = "\033[1;33m"
    CYAN   = "\033[0;36m"
    BOLD   = "\033[1m"
    NC     = "\033[0m"

def info(msg):    print(f"{C.CYAN}[INFO]{C.NC}  {msg}")
def success(msg): print(f"{C.GREEN}[OK]{C.NC}    {msg}")
def warn(msg):    print(f"{C.YELLOW}[WARN]{C.NC}  {msg}")
def header(msg):  print(f"\n{C.BOLD}{msg}{C.NC}\n" + "─" * 50)

def error(msg):
    print(f"{C.RED}[ERROR]{C.NC} {msg}", file=sys.stderr)
    sys.exit(1)

# ── 工具函数 ──────────────────────────────────────────────────
def run(cmd: list[str]):
    """运行命令，失败时退出"""
    result = subprocess.run(cmd)
    if result.returncode != 0:
        error(f"命令失败: {' '.join(cmd)}")

def get_version(csproj: str) -> str:
    with open(csproj, encoding="utf-8") as f:
        m = re.search(r"<Version>([^<]+)</Version>", f.read())
    if not m:
        error(f"无法从 {csproj} 读取版本号")
    return m.group(1)

# ════════════════════════════════════════════════════════════════
# 功能 1：列出本地 NuGet 源
# ════════════════════════════════════════════════════════════════
def list_sources():
    header("📋 当前 NuGet 源列表")
    run(["dotnet", "nuget", "list", "source"])

# ════════════════════════════════════════════════════════════════
# 功能 2：设置推送目标源
# ════════════════════════════════════════════════════════════════
def set_source() -> str:
    header("🔧 设置推送源")
    print("常用源：")
    print("  1) nuget.org          (https://api.nuget.org/v3/index.json)")
    print("  2) GitHub Packages    (https://nuget.pkg.github.com/<OWNER>/index.json)")
    print("  3) 自定义源（手动输入）")
    print()

    choice = input("请选择 [1/2/3]: ").strip()

    if choice == "1":
        source = "https://api.nuget.org/v3/index.json"
    elif choice == "2":
        owner = input("请输入 GitHub 用户名/组织名: ").strip()
        if not owner:
            error("用户名不能为空")
        source = f"https://nuget.pkg.github.com/{owner}/index.json"
    elif choice == "3":
        source = input("请输入自定义源 URL: ").strip()
        if not source:
            error("源 URL 不能为空")
    else:
        error("无效选项")

    success(f"推送源已设置为: {source}")
    return source

# ════════════════════════════════════════════════════════════════
# 功能 3：手动设置 API Key
# ════════════════════════════════════════════════════════════════
def set_api_key() -> str:
    header("🔑 设置 API Key")
    api_key = getpass.getpass("请输入 API Key（输入不可见）: ")
    if not api_key:
        error("API Key 不能为空")
    success("API Key 已设置")
    return api_key

# ════════════════════════════════════════════════════════════════
# 打包
# ════════════════════════════════════════════════════════════════
def build_and_pack(version: str):
    header(f"📦 打包 (版本 {version})")

    os.makedirs(OUTPUT_DIR, exist_ok=True)
    for f in glob.glob(os.path.join(OUTPUT_DIR, "*.nupkg")):
        os.remove(f)

    info("1/2 构建并打包 BitSerializer.Generator...")
    run(["dotnet", "pack", GENERATOR_PROJ,
         "-c", "Release",
         "--output", OUTPUT_DIR,
         f"-p:Version={version}"])

    info("2/2 构建并打包 BitSerializer...")
    run(["dotnet", "build", GENERATOR_PROJ,
         "-c", "Release",
         f"-p:Version={version}"])
    run(["dotnet", "pack", MAIN_PROJ,
         "-c", "Release",
         "--output", OUTPUT_DIR,
         f"-p:Version={version}"])

    pkgs = glob.glob(os.path.join(OUTPUT_DIR, "*.nupkg"))
    print()
    success(f"打包完成，输出目录: {OUTPUT_DIR}")
    for p in pkgs:
        print(f"    {os.path.basename(p)}")

# ════════════════════════════════════════════════════════════════
# 推送
# ════════════════════════════════════════════════════════════════
def push_packages(source: str, api_key: str):
    header(f"🚀 推送包到 {source}")

    pkgs = glob.glob(os.path.join(OUTPUT_DIR, "*.nupkg"))
    if not pkgs:
        warn("未找到任何 .nupkg 文件，请先打包")
        return

    for pkg in pkgs:
        info(f"推送 {os.path.basename(pkg)} ...")
        run(["dotnet", "nuget", "push", pkg,
             "--source", source,
             "--api-key", api_key,
             "--skip-duplicate"])
        success(f"{os.path.basename(pkg)} 推送成功")

# ════════════════════════════════════════════════════════════════
# 主菜单
# ════════════════════════════════════════════════════════════════
def main():
    version = get_version(MAIN_PROJ)

    pad = 29 - len(version)
    print(f"{C.BOLD}")
    print("╔══════════════════════════════════════╗")
    print("║   BitSerializer 打包 & 推送工具       ║")
    print(f"║   版本: {version}{' ' * pad}║")
    print("╚══════════════════════════════════════╝")
    print(C.NC, end="")

    print("请选择操作：")
    print("  1) 仅列出 NuGet 源")
    print("  2) 仅打包")
    print("  3) 打包并推送（交互式配置源和 API Key）")
    print("  q) 退出")
    print()

    action = input("请选择 [1/2/3/q]: ").strip().lower()

    if action == "1":
        list_sources()
    elif action == "2":
        build_and_pack(version)
    elif action == "3":
        list_sources()
        source  = set_source()
        api_key = set_api_key()
        build_and_pack(version)
        push_packages(source, api_key)
        print()
        success("全部完成！")
    elif action == "q":
        print("已退出。")
        sys.exit(0)
    else:
        error("无效选项")


if __name__ == "__main__":
    main()