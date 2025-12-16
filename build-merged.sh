#!/bin/bash

# Emby Plugin Danmu - 合并打包脚本
# 此脚本会发布项目并使用 ILRepack 合并依赖 DLL 到主 DLL 中

set -e  # 遇到错误立即退出

PROJECT_DIR="Emby.Plugin.Danmu"
PUBLISH_DIR="${PROJECT_DIR}/bin/Release/net8.0/publish"
OUTPUT_DIR="dist"

echo "=========================================="
echo "Emby Plugin Danmu - 合并打包脚本"
echo "=========================================="

# 1. 检查 dotnet 是否安装
if ! command -v dotnet &> /dev/null; then
    echo "错误: 未找到 dotnet 命令，请先安装 .NET SDK"
    exit 1
fi

# 设置 DOTNET_ROOT（ilrepack 需要）
if [ -z "$DOTNET_ROOT" ]; then
    # 尝试从 dotnet --info 获取 Base Path
    DOTNET_BASE_PATH=$(dotnet --info 2>/dev/null | grep -i "base path" | head -1 | sed 's/.*Base Path:[[:space:]]*//' | sed 's/[[:space:]]*$//')
    if [ -n "$DOTNET_BASE_PATH" ]; then
        # Base Path 通常是 .../libexec/sdk/版本，DOTNET_ROOT 应该是 libexec 目录
        DOTNET_ROOT=$(echo "$DOTNET_BASE_PATH" | sed 's|/sdk/.*||')
        if [ -d "$DOTNET_ROOT" ]; then
            export DOTNET_ROOT
            echo "信息: 设置 DOTNET_ROOT=$DOTNET_ROOT"
        fi
    fi
    
    # 如果仍然没有设置，尝试从 dotnet 路径推断
    if [ -z "$DOTNET_ROOT" ]; then
        DOTNET_PATH=$(which dotnet)
        if [ -n "$DOTNET_PATH" ]; then
            DOTNET_BIN_DIR=$(dirname "$DOTNET_PATH")
            # 检查是否是 Homebrew 安装
            if [[ "$DOTNET_BIN_DIR" == *"/opt/homebrew/opt/dotnet"* ]] || [[ "$DOTNET_BIN_DIR" == *"/usr/local/opt/dotnet"* ]]; then
                # Homebrew 安装：DOTNET_ROOT 应该是 libexec 目录
                DOTNET_ROOT=$(dirname "$DOTNET_BIN_DIR")/libexec
            else
                # 标准安装：DOTNET_ROOT 通常是 dotnet 的父目录
                DOTNET_ROOT=$(dirname "$DOTNET_BIN_DIR")
            fi
            
            if [ -d "$DOTNET_ROOT" ]; then
                export DOTNET_ROOT
                echo "信息: 设置 DOTNET_ROOT=$DOTNET_ROOT"
            fi
        fi
    fi
fi

# 2. 检查 ilrepack 是否安装
ILREPACK_CMD=""
ILREPACK_PATH=""

# 尝试找到 ilrepack
if command -v ilrepack &> /dev/null; then
    ILREPACK_PATH=$(which ilrepack)
elif [ -f "$HOME/.dotnet/tools/ilrepack" ]; then
    ILREPACK_PATH="$HOME/.dotnet/tools/ilrepack"
    export PATH="$HOME/.dotnet/tools:$PATH"
fi

# 如果找不到，尝试安装
if [ -z "$ILREPACK_PATH" ]; then
    echo "警告: 未找到 ilrepack 命令，尝试安装..."
    dotnet tool install -g dotnet-ilrepack || {
        echo "错误: 无法安装 dotnet-ilrepack，请手动安装:"
        echo "  dotnet tool install -g dotnet-ilrepack"
        exit 1
    }
    export PATH="$HOME/.dotnet/tools:$PATH"
    if [ -f "$HOME/.dotnet/tools/ilrepack" ]; then
        ILREPACK_PATH="$HOME/.dotnet/tools/ilrepack"
    elif command -v ilrepack &> /dev/null; then
        ILREPACK_PATH=$(which ilrepack)
    fi
fi

# 验证 ilrepack 是否存在
if [ -z "$ILREPACK_PATH" ] || [ ! -f "$ILREPACK_PATH" ]; then
    echo "错误: 无法找到 ilrepack 可执行文件"
    exit 1
fi

# 设置 ILREPACK_CMD（使用完整路径）
ILREPACK_CMD="$ILREPACK_PATH"
echo "信息: 使用 ilrepack: $ILREPACK_CMD"

# 3. 清理旧的发布目录
echo ""
echo "步骤 1: 清理旧的发布目录..."
rm -rf "${PUBLISH_DIR}"
rm -rf "${OUTPUT_DIR}"

# 4. 发布项目
echo ""
echo "步骤 2: 发布项目..."
dotnet publish -c Release "${PROJECT_DIR}/${PROJECT_DIR}.csproj"

if [ ! -d "${PUBLISH_DIR}" ]; then
    echo "错误: 发布目录不存在: ${PUBLISH_DIR}"
    exit 1
fi

# 5. 进入发布目录
cd "${PUBLISH_DIR}"

# 6. 查找需要合并的 DLL
MAIN_DLL="Emby.Plugin.Danmu.dll"
DEPENDENCY_DLLS=(
    "Google.Protobuf.dll"
    "RateLimiter.dll"
    "ComposableAsync.Core.dll"
    "ICSharpCode.SharpZipLib.dll"
)

echo ""
echo "步骤 3: 检查依赖 DLL..."
MISSING_DLLS=()
for dll in "${DEPENDENCY_DLLS[@]}"; do
    if [ -f "$dll" ]; then
        echo "  ✓ 找到: $dll"
    else
        echo "  ✗ 缺失: $dll"
        MISSING_DLLS+=("$dll")
    fi
done

if [ ${#MISSING_DLLS[@]} -gt 0 ]; then
    echo "警告: 以下 DLL 未找到，将跳过合并:"
    printf '  - %s\n' "${MISSING_DLLS[@]}"
fi

# 7. 备份原始 DLL
if [ -f "$MAIN_DLL" ]; then
    echo ""
    echo "步骤 4: 备份原始 DLL..."
    cp "$MAIN_DLL" "${MAIN_DLL}.bak"
    echo "  ✓ 已备份: ${MAIN_DLL}.bak"
fi

# 8. 构建合并命令
echo ""
echo "步骤 5: 合并 DLL..."
EXISTING_DLLS=("$MAIN_DLL")
for dll in "${DEPENDENCY_DLLS[@]}"; do
    if [ -f "$dll" ]; then
        EXISTING_DLLS+=("$dll")
    fi
done

MERGED_DLL="Emby.Plugin.Danmu.merged.dll"

# 执行合并
"$ILREPACK_CMD" /wildcards /parallel /ndebug /target:library \
    /out:"${MERGED_DLL}" \
    "${EXISTING_DLLS[@]}" || {
    echo "错误: ILRepack 合并失败"
    echo "回退: 恢复原始 DLL..."
    if [ -f "${MAIN_DLL}.bak" ]; then
        mv "${MAIN_DLL}.bak" "$MAIN_DLL"
    fi
    exit 1
}

# 9. 替换原始 DLL
if [ -f "$MERGED_DLL" ]; then
    echo ""
    echo "步骤 6: 替换原始 DLL..."
    mv "$MERGED_DLL" "$MAIN_DLL"
    echo "  ✓ 合并完成: $MAIN_DLL"
    
    # 显示文件大小
    ORIGINAL_SIZE=$(stat -f%z "${MAIN_DLL}.bak" 2>/dev/null || stat -c%s "${MAIN_DLL}.bak" 2>/dev/null || echo "0")
    MERGED_SIZE=$(stat -f%z "$MAIN_DLL" 2>/dev/null || stat -c%s "$MAIN_DLL" 2>/dev/null || echo "0")
    echo "  原始大小: $(numfmt --to=iec-i --suffix=B $ORIGINAL_SIZE 2>/dev/null || echo "${ORIGINAL_SIZE} bytes")"
    echo "  合并大小: $(numfmt --to=iec-i --suffix=B $MERGED_SIZE 2>/dev/null || echo "${MERGED_SIZE} bytes")"
fi

# 10. 清理不需要的文件
echo ""
echo "步骤 7: 清理不需要的文件..."
# 删除合并的依赖 DLL（保留备份）
for dll in "${DEPENDENCY_DLLS[@]}"; do
    if [ -f "$dll" ]; then
        rm -f "$dll"
        echo "  ✓ 已删除: $dll"
    fi
done

# 11. 创建输出目录
cd - > /dev/null
mkdir -p "${OUTPUT_DIR}"

# 12. 复制文件到输出目录
echo ""
echo "步骤 8: 复制文件到输出目录..."
cp -r "${PUBLISH_DIR}"/* "${OUTPUT_DIR}/" 2>/dev/null || {
    # 如果 cp -r 失败，尝试逐个复制
    find "${PUBLISH_DIR}" -type f -exec cp {} "${OUTPUT_DIR}/" \;
}

echo ""
echo "=========================================="
echo "✓ 合并打包完成！"
echo "=========================================="
echo ""
echo "输出目录: ${OUTPUT_DIR}/"
echo ""
echo "下一步:"
echo "  1. 将 ${OUTPUT_DIR}/ 目录下的所有文件复制到 Emby 的 /config/plugins/ 目录"
echo "  2. 重启 Emby 服务"
echo ""
echo "注意: 如果合并后的 DLL 在 Emby 中无法正常工作，"
echo "      请使用 ${PUBLISH_DIR}/ 目录中的原始文件（包含所有依赖 DLL）"
echo ""

