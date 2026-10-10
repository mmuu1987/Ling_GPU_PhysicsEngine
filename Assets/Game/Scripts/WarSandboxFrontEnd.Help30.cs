using UnityEngine;
namespace MassEngine.Game {
 public sealed partial class WarSandboxFrontEnd {
  private static float NavigationWidth30=>Guide30.For(WarSandboxSceneSession.Instance)!=null?264:NavWidth;
  private void OpenHelp30()=>Guide30.For(session)?.Open();
  private void DrawHelp30(){var guide=Guide30.For(session);if(guide==null)return;
   ui.Panel("help30-shade",new Rect(0,0,ui.Width,ui.Height),WarSandboxUGUI.Shade);
   float w=Mathf.Min(900,ui.Width-40),h=570,x=(ui.Width-w)/2,y=Mathf.Max(12,(ui.Height-h)/2);ui.Panel("help30-card",new Rect(x,y,w,h));
   ui.Code("help30-code",new Rect(x+24,y+18,w-48,20),guide.Intro?"FIRST STEPS // 首次布阵":"FIELD GUIDE // 操作帮助",12,WarSandboxUGUI.Accent);
   ui.Label("help30-title",new Rect(x+14,y+42,w-28,44),guide.Intro?"先编成，再布阵，最后开战":"随时查看，按需使用",26,WarSandboxUGUI.Ink,true);
   string[] tabs={"快速上手","战斗与镜头","方案与战后"};float tw=(w-64)/3;
   for(int i=0;i<3;i++){int page=i;ui.Button("help30-tab-"+i,new Rect(x+24+i*(tw+8),y+100,tw,36),tabs[i],()=>guide.Select(page),false,true,guide.Page==i);}
   string body=guide.Page==0?
    "1  编成：点击左侧军团的“军团详情”，调整人数或兵种。\n     修改后点击“完成返回”，回到布阵总览。\n2  布阵：点击军团旁的“布阵”选中编成，再用“点击放置”调整位置。\n3  开战：检查双方人数和位置，再点击“开战”。\n\n“应用布阵”只让当前配置在本局生效，不会自动保存为方案。\n想下次继续使用，请到“方案库”主动保存。\n这份帮助可从主菜单、战场右上角或“更多 / 帮助”再次打开。":guide.Page==1?
    "选择与指令：点击军团，或用数字键 1–9 按军团顺序选择；快速双击数字键 = 选中并聚焦，Tab / Shift+Tab 轮换军团。\n移动：按 M 后点击地面；再次按 M，Shift + 点击追加航点。\nEsc / 取消选点只取消选点，不清除已有命令。\nSpace 暂停 / 继续；下达新指令会继续运行。\n镜头：中键拖动平移；滚轮缩放；按住右键拖动转向。\n按住右键 + WASD 移动，Q / E 下降 / 上升；方向键直接平移；Ctrl 慢速微调。\nF 查看/跟随所选军团；F3 全景；Home 回初始视角；速度与取景可在 设置 → 镜头 调整。\n小地图：左键定位，右键下令；镜头/指令快捷键不用于编辑输入框。":
    "草稿修改 ≠ 应用到本局 ≠ 保存为本地方案。\n保存方案不会自动开战；载入方案后仍需检查并应用。\n离开未保存的自定义配置时会提醒，取消可回去另存。\n\n“沿用配置再战”保留已应用编成和布阵，清除上一局进度与临时指令。\n“调整编成布阵”回到已应用配置，可继续修改；未应用草稿不用于再战。\n“选择其他战场”返回目录，不会自动迁移当前配置。\n方案不是战斗进度存档；强制结束进程不属于离开提醒的保护范围。";
   ui.Label("help30-body",new Rect(x+18,y+160,w-36,300),body,16,WarSandboxUGUI.Ink);
   ui.Label("help30-note",new Rect(x+18,y+468,w-36,42),guide.Error??(guide.Intro?"可直接跳过；完成或跳过后不再自动弹出，操作帮助仍可随时打开。":"查看帮助会暂停战斗并取消未完成选点；关闭后恢复原运行/暂停状态。已下达命令不变。"),13,guide.Error!=null?WarSandboxUGUI.Danger:WarSandboxUGUI.Muted);
   ui.Button("help30-close",new Rect(x+w-264,y+518,240,36),guide.Intro?"知道了，开始布阵":"关闭帮助",guide.Close,true);
   if(guide.Intro)ui.Button("help30-skip",new Rect(x+24,y+518,180,36),"跳过引导",guide.Close);
  }
 }
}
