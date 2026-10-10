# 商店素材下载进度（2026-10-04）

目标：Supercyan / Environment Pack: Free Forest Sample，商品168396。
当前结果：未下载成功，无可用unitypackage，未导入工程或修改游戏场景。

检查与尝试：
- 用户提供的本机凭证文件已找到，为Cookie请求头格式；未复制凭证内容到交付文件。
- 官方商店session接口可识别网页登录会话，但这不等于下载授权有效。
- Unity包下载信息接口返回401；官方网页代码对应的浏览器下载接口 /api/downloads/168396 返回403。
- 本机已检查的Asset Store默认缓存与_DownloadedModels目录未发现首选森林包。
- 已将凭证文件加入本机.git/info/exclude，避免误提交；未commit/push。

下一步需要用户在官方商品页确认资源已加入My Assets，并在Unity Hub/Editor登录同一账号；如需许可确认或重新登录，由用户在官方界面完成。不能断言403一定由未领取导致，也不能把网页登录成功当作下载成功。不要索取新的Cookie或密码，不绕过许可/下载授权检查。

商品：https://assetstore.unity.com/packages/3d/vegetation/environment-pack-free-forest-sample-168396
验证回执：Logs/AssetStoreDownload-20261004/ 下环境、访问、下载结果JSON；均不含凭证值。
