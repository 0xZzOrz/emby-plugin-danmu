# emby-plugin-danmu

[![releases](https://img.shields.io/github/v/release/0xZzOrz/emby-plugin-danmu)](https://github.com/0xZzOrz/emby-plugin-danmu/releases)
[![emby](https://img.shields.io/badge/emby-4.9.1.90-lightgrey?logo=emby)](https://github.com/cxfksword/emby-plugin-danmu/releases)
[![LICENSE](https://img.shields.io/github/license/cxfksword/emby-plugin-danmu)](https://github.com/0xZzOrz/emby-plugin-danmu/blob/main/LICENSE) 

Emby 弹幕自动下载插件（基于 .NET 8，适配 Emby 4.9+），已支持的弹幕来源：B站，弹弹play，优酷，爱奇艺，腾讯视频，芒果TV，弹幕API。

支持功能：

* 自动下载xml格式弹幕
* 生成ass格式弹幕
* 支持api访问弹幕
* 兼容弹弹play接口规范访问

![logo](doc/logo.png)

## 安装插件

1) 手动安装（推荐）

- 从 Release 或本地构建产物中获取 `dist/Emby.Plugin.Danmu.dll` 文件
- 拷贝到 Emby 插件目录：`/config/plugins/`（容器）或 `<Emby数据目录>/plugins/`
- 重启 Emby

2) 构建后手动部署

- 在仓库根目录执行 `./build-merged.sh`
- 完成后将 `dist/Emby.Plugin.Danmu.dll` 文件拷贝到 Emby 插件目录并重启 Emby

## 如何使用

1. 安装后，进入`控制台 -> 插件`，查看下`Danmu`插件是否是**Active**状态
2. 进入`控制台 -> 媒体库`，点击任一媒体库进入配置页，在最下面的`字幕下载`选项中勾选**Danmu**，并保存

   <img src="doc/tutorial.png"  width="720px" />

3. 新加入的影片会自动获取弹幕（番剧/电影），旧影片可执行计划任务 **扫描媒体库匹配弹幕**
4. 若匹配错误，可在影片详情使用 **修改字幕** 重新搜索
5. 电视剧/动画需保证每季集数正确并填写集号
6. 生成 ASS 需在插件配置中打开（默认关闭）
  
> B站电影或季元数据也支持手动指定BV/AV号，来匹配UP主上传的视频弹幕。多P视频和剧集是按顺序一一对应匹配的，所以保证emby中剧集有正确的集号很重要

## 支持的api接口

* `/api/danmu/{id}`:  获取emby电影或剧集的xml弹幕链接，不存在时，url为空
* `/api/danmu/{id}/raw`:  获取emby电影或剧集的xml弹幕文件内容
* `/api/v2/search/anime?keyword=xxx`: 根据关键字搜索影视
* `/api/v2/search/episodes?anime=xxx`: 根据关键字搜索影视的剧集信息
* `/api/v2/bangumi/{bangumiId}`: 获取影视详细信息
* `/api/v2/comment/{episodeId}?format=xml`: 获取弹幕内容，默认json格式

## 如何播放

xml格式：

* [switchfin](https://github.com/dragonflylee/switchfin) (Windows/Mac/Linux) 🌟
* [Senplayer](https://apps.apple.com/us/app/senplayer-video-media-player/id6443975850) (iOS/iPadOS/AppleTV) 🌟
* [弹弹play](https://www.dandanplay.com/) (Windows/Mac/Android)
* [KikoPlay](https://github.com/KikoPlayProject/KikoPlay) (Windows/Mac)


ass格式：

* PotPlayer (Windows)
* IINA (Mac)
* Infuse (Mac/iOS/iPadOS/AppleTV)


## How to build

1. Clone or download this repository

2. Ensure you have .NET SDK 8.0 installed

3. Build (两种方式)

```sh
# 简单发布
dotnet publish --configuration=Release Emby.Plugin.Danmu/Emby.Plugin.Danmu.csproj

# 一键构建并合并依赖到 dist/
./build-merged.sh
```


## How to test

1. Build the plugin

2. 将 `dist/Emby.Plugin.Danmu.dll`（或 publish 输出目录）文件拷贝到 Emby `data/plugins` 目录

## Thanks

[downkyi](https://github.com/leiurayer/downkyi)

[cxfksword](https://github.com/cxfksword/jellyfin-plugin-danmu)

[fengymi](https://github.com/fengymi/emby-plugin-danmu)

## 免责声明

本项目代码仅用于学习交流编程技术，下载后请勿用于商业用途。

如果本项目存在侵犯您的合法权益的情况，请及时与开发者联系，开发者将会及时删除有关内容。
