# Bob 五笔 实现笔记

[README](../README.md) 讲怎么用，这份讲怎么实现：每个功能的判定落在哪个类型、为什么这么定、踩过什么坑。
改功能时记得同步托盘「说明」里的文案（`TrayDialogs.Usage`），那边写给用户看，容易和代码脱节。

## 目录

- `src/BobIme.Core` 码表加载、前缀查询、组码状态机。不碰窗口也不碰键盘，可以单测。
- `src/BobIme.Shell` 钩子、候选窗、符号面板、设置界面、配置读写（`AppSettings`）、光标定位、前台窗口事件（`FocusWatcher`）、SendInput 注入、托盘常驻（右键菜单：符号输入、标点档二选一、输入档四选一、设置、说明、关于、退出）。
- `tests/BobIme.Core.Tests` 单测。它同时引用 Core 和 Shell，两边都不走接口，所以被单测摸到的逻辑一律得是 `public`。
- `tools/BobIme.Probe` 命令行敲码工具，调编码、看候选顺序用。
- `tools/BobIme.Icons` 从源图生成 `assets\*.ico`，带水印裁剪和徽章包围盒测量。
- `data/dict` 两份码表，见下。
- `installer` Inno Setup 脚本和出包脚本。
- `.github/workflows/release.yml` 推 `v*` 标签就出安装包并发 Release。

## 码表

| 文件 | 来源 | 列 | 说明 |
| --- | --- | --- | --- |
| `jidian86.tsv` | 极点 86 词库（rime-wubi86-jidian 主表 + 扩展表） | `词\t码` | 91462 条，五笔最长 4 码 |
| `pinyin_base.tsv` | rime-ice `base` + `8105` + `41448` | `词\t拼音\t词频` | 395529 条，拼音连写最长 16 字母 |

两份都不入库前先过一遍规则：

- 挂在编码上的中文标点、声调符号、私用区字符一律不加载。中文标点由标点档换键输出、不靠词条，
  这些词条只会变成空白候选，还白占 `cobd`、`nhty`、`xyxy` 这类正常编码位。
- 单个拉丁字母词条也不加载：`ā á ǎ à a` 整块挂在 `copa`、`copu` 上，
  `cosx` 上只留一个 `π`。
- 日语假名整块不加载：平假名挂在 `cojp`、片假名挂在 `copj`。
- `cop*` 那一块剩下的就是 `勹 灬 冫 艹 屮` 这类部首字，是要打生僻字的底子，暂时留着。
- 五笔表是把 rime 的 `*.dict.yaml` 去掉 yaml 头和权重列拼起来的：主表在前、扩展表在后，同一个「词+码」只留先出现的。
  同码内的行序就是候选次序（`CodeTable` 按行号排，源表内部本来就按权重降序），所以转换时绝不重排。
  重新生成用 `perl tools/conv_wubi.pl <输出> <源表...>`。
- 拼音三张源表合并成一个 TSV：音节列去掉空格、带词频列但词频低于 100 的丢掉、
  词长超过 8 字或码长超过 16 字母的丢掉，同形同码只留词频高的那条。
  重新生成用 `perl tools/conv_pinyin.pl <输出> <源表...>`。

## 混输规则

混合输入档下一次按键同时查两套码表，候选按「五笔段在前、拼音段在后」拼成一页
（纯五笔、纯拼音档只挂一套表，名额全给那一套）：

- 每套码表先保底分到半页（`PageSize` 9 → 各 5 / 4），没用完的名额顺延给下一套，
  免得五笔的上千条同前缀词把拼音挤没。
- 五笔表按码长分带二分查，简码先出；拼音表按首字母切成 26 桶、桶内词频降序，一次按键只扫前缀
  所在的桶（最大 3.6 万条），所以不给扫描设上限：`pofang` 的「破防」全局词频排在 7.9 万位也出得来。
  实测多数前缀在 0.2 ms 以内，最坏一次按键 1 ms（`tangping` 整桶扫完才 4 条命中）。
- 编码超过 4 个字母后五笔表自然无解，这一段就只剩拼音候选。
- 一次按键只缓存 5 页（`PageLimit` 45 条），翻页键（默认 `[` / `]`，设置里可换成四组）前后翻；
  名额是按页分的，所以第 2 页还是五笔 5 条接拼音 4 条，不会翻着翻着某一类整个消失。
  候选条编码右侧标「2/5」，只有一页时不标。翻到边界也照样吃掉这两个键，
  免得落到「其他按键」分支把首选顺手顶上屏、还漏一个翻页字符进目标程序。
- 鼠标点页码右边的 `◀ ▶` 走的是同一条路（`ImeController.TurnPage`），热区 26×30
  ——往下把行间那点空也吃了，不然 20px 高的一点根本点不中；画出来的那一格只占
  编码行，和页码数字齐高，居中到整块热区会看着像往下坠。悬停有底色，
  到边界的那一档置灰且不响应。
- 窗口位置只在起新编码（或窗口刚露出来）时按光标重定位；翻页只换内容不动位置，
  免得点一下箭头窗体就跳一下。每页候选长短不同会让宽度变，所以不定位的那条路
  还要过一次 `CaretLocator.Keep`，把变宽后的右缘夹回工作区。
  箭头排在编码和页码后面，不贴窗口右缘——贴右缘的话宽度一变它就跟着跑。
- 唯一全码命中且整套候选只剩一条时才自动上屏，拼音段还有候选就不抢；但四码不算，
  四码一律等空格确认（`AutoCommitCodeLimit`）。看的是整段命中数，不是当前页条数，
  否则一页只剩一条就会把抢上屏带回来。
- 候选条上不给拼音段写标签：整段降成次要灰、前面点一个间隔点，比「｜拼音」两个字安静。
  两段是分开两次画的，拼音段的起点 x 就是五笔段量出来的宽度。

## 输入档

托盘右键菜单下半组四选一：英文输入 / 五笔输入 / 拼音输入 / 混合输入，选中项前面是实心圆点。

- 英文档就是中文模式关掉（`ImeController.ChineseMode = false`），和左 Shift 单敲是同一个状态，
  两边切换圆点都会跟着走；另三档决定挂哪套码表。
- 码表档就是 `ImeEngine.ActiveSource` 设成 `Wubi`、`Pinyin` 或 `null`（混输）。
  候选名额分配和码长上限都跟着挂上的表走——纯五笔四位顶满，纯拼音能连写到 16 个字母。
- 换档会丢掉手上半截编码：编码规则变了，留着只会拼错。
- 点档位顺手把总开关开回来，免得点完打字没反应像是坏了；「启用/停用输入法」热键（默认 `Ctrl+Space`）
  还是启停开关，菜单上不再有「启用」那一项。
- 圆点不另外记账，只看控制器现在的 `ChineseMode` 和 `ActiveSource`，所以不会出现
  按了 Shift 而菜单还亮着旧档的情况。
- 没找到拼音码表时，拼音输入和混合输入两项置灰不可选，启动时直接停在五笔输入档。

## 按程序定档

设置里一张规则表 `WindowRules`，一行一个程序：程序名 + 它获得焦点时要用的档位（英文档 / 中文档）。
中文档只把中文模式打开，码表档仍旧用当前那一档——`ActiveSource` 在英文档期间没被动过，
所以顶回中文就是回到用户自己那套五笔/拼音/混合。表里没有的窗口不插手。

- 换前台窗口用 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 听，不轮询：切窗口不多，但每次都得当场办完，
  晚了的话用户敲下的第一个字母已经进了候选窗。回调跑在自家消息循环上（`WINEVENT_OUTOFCONTEXT`），
  所以 `_held` 不用加锁。`FocusWatcher` 只管把新前台句柄递出来，进程名和换档都在 `ImeApplicationContext`。
- 进程名取 `GetWindowThreadProcessId` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` →
  `QueryFullProcessImageNameW`（`WindowInfo.ExeName`）。以管理员权限运行的程序读不到路径，
  那种窗口本来也收不到钩子的按键（UIPI），规则表对它们同样无从生效。
- 比对和整理都在 `WindowRule`：程序名只认文件名、忽略扩展名与大小写、两边都去空白，所以填 `wow`、
  `Wow.exe`、贴进来的完整路径算同一个；`Normalize` 顺手去重并截到 `Max`（32）条，
  `Duplicates` 单独给设置界面用——保存时撞车要提示，不能悄悄丢掉后一条。
- 目标档在 `ModeOnTarget`：查表拿到那一条的档位，英文档给 `InputMode.English`，中文档给「中文模式开着、
  码表档原样」，表里没有就返回 `null` 不插手。一个程序在表里只有一条，所以不存在两张名单那种谁说了算的问题。
- 表里的程序获得焦点：`ModeOnFocus.ShouldApply` 决定要不要切——不在目标档、而且这个窗口不是刚才顶过档的那一个才切。
  切之前用 `_held ??= CurrentMode` 记下用户自己那一档（只在第一次顶档时记，表里两个程序来回切不能被上一个窗口顶的档盖掉）、
  把顶上来的那档记进 `_forced`、把窗口句柄记进 `_forcedWindow`。
  同一个窗口再报一次事件不重顶，用户在里头手动切过的那一次才留得住。离开表：`ModeToRestore` 只在
  当前还是 `_forced` 那一档时才还 `_held`，用户在窗口里手动切过就以他那次为准，不覆盖。
  这里踩过一次坑：原来用「`_held` 是不是空」当闸门，WoW（英文档）↔ 微信（中文档）来回切时，
  进微信把 `_held` 记成了英文档，第二次进 WoW 就被这道闸门挡掉、再也不顶英文；
  记事本不在表里、走的是「离开表」那条会把 `_held` 清空，所以只有两张规则之间才复现。
- 自家窗口（设置界面、说明、关于、消息框、候选窗）一律跳过：它们是弹出来给人打字的，
  跟着规则表走会把用户正在填的内容搅掉。
- 任务栏、托盘溢出、开始菜单、桌面这类 shell 窗口也跳过（`ShellWindows.IsShell` 比类名）。
  点托盘就会让它们成为前台，当成「离开表」的话，菜单还没打开档位就先还回去了；
  「填成一条规则」也会永远停在 `explorer.exe`。类名表按这台机器实测的窗口定，
  表里填 `explorer.exe` 仍旧管得住资源管理器窗口（`CabinetWClass`）。
  Alt+Tab 期间 explorer.exe 还会报 `ForegroundStaging`、`XamlExplorerHostIslandWindow`、
  `ApplicationManager_DesktopShellWindow` 这几个过场窗口，而且**会夹在真窗口事件后面再报一次**；
  落在后面那一次要是当成「离开表」，刚顶上去的档位当场就被还回去，所以三个都必须在名单里。
  2026-11-21 就是靠另一个进程自己挂 `SetWinEventHook` 采了 75 秒前台事件才看清这个先后的。
- 开机时已经在前台的那个窗口不等事件，构造函数里自己补一次 `OnForegroundChanged`。
- 表为空等于没这个功能；`FocusWatcher` 装不上也只是规则表不生效，输入法本身照常用。

## 标点档

菜单上半组二选一：英文标点 / 中文标点，状态就是 `ImeController.ChinesePunctuation`。

- 中文标点档把标点键换成全角自己送出去，所以这个键要被吞掉；英文标点档一个都不碰，
  半角全角由目标程序自己决定。英文模式在两道闸门之前，标点档自然不生效。
- 对照表在 `Punctuator`。未按 Shift：`, → ，`、`. → 。`、`; → ；`、`` ` → · ``、`\ → 、`。
- 按住 Shift：`< → 《`、`> → 》`、`? → ？`、`: → ：`、`~ → ～`、`| → ｜`、`_ → ——`、
  `1 → ！`、`6 → ……`、`8 → ×`、`9 → （`、`0 → ）`。
- 只按 Shift 才算标点，Ctrl/Alt 组合仍旧先放行、编码不动；表里没有的键（`@` `#` `%`、大写字母）照旧漏过去。

## 符号面板

托盘菜单最上面「符号输入」，点开 `SymbolForm`：按设置里的「出现位置」摆在屏幕正中（工作区居中）或贴着光标，
是一块分组格子，点一下就把符号送进正在打字的程序。分组在 `Symbols`，画和点都在 `SymbolForm`，格子按 16 列排、
不滚动也不分页；高度按各组里最多的行数一次定死，换分组不动窗口尺寸，面板也就不会跟着挪。第一组「工程」是机械制图常用
符号：直径 `⌀`、角度和素数 `°′″`、形位公差 `⏤▱○◎⊙⊕`、沉孔锪孔 `⌴⌵`、`㎜㎛㎫` 这类单位。

- 面板不激活，所以点它不会把前台抢走，`SendInput` 的字仍旧落在他的光标上（见「不抢焦点的浮层」）。
- 点格子只上屏、不关面板，连着点几个就出几个；× 关闭，标题栏可以拖，但重新打开仍旧回到设置里那个位置。
- 打开面板前会先把候选窗收起来，免得两层浮窗叠在一起。
- 引号 `0xDE` 一个键位要出一开一合，`Punctuator` 里记一个 `_quoteOpen` 轮流，单双引号共用这一次翻转。
- 方括号不在表里：`[` `]` 是翻页键，中文标点档也不能把它们抢去当【】；`Shift+[`（也就是 `{`）
  既不在表里也不翻页，照旧放行。
- 编码没挑完时先上屏第一候选再接标点，两段拼成一次 `SendInput`，中间不会插进别的字。
- 标点档只管换键，不碰总开关，也不清当前编码——点了标点档接着打刚才那串码还是原来的候选。

## 大写英文

大写字母一律不当编码，原样交给目标程序：

- 按住 Shift 敲的字母由钩子里自己记录的修饰键状态判定，直接放行。
- Caps Lock 开着敲出来的也是大写，同样放行。这个状态每次按键现读
  `GetKeyState(VK_CAPITAL)`，不自己维护影子状态，切灯之后立刻生效。
- 放行时不清编码缓冲，所以不会把他还没挑完的词顺手顶上屏；
  而按下 Caps Lock 那一下属于未指派按键，会先把当前首选词上屏，
  于是「中CPU」这类混排的顺序是自然的。

## 中/英切换

默认单敲左 Shift 切中/英（这个键在设置里能换），和系统输入法的手感一致：

- 「单敲」的判定在钩子里记账：左 Shift 按下后，抬起之前只要碰过任何一个别的键就不算，
  于是 Shift 打大写字母、Ctrl+Shift 切布局都不会误触。判定只看被绑成单敲的那一侧抬起，
  另一侧（默认右 Shift）单敲不切。
- 记账和回调都跑在钩子回调里，所以切模式这件事是 `Post` 回消息循环做的，回调本身只投递。
- 英文模式一个键都不吃，全交回目标程序；「启用/停用」热键的判断抢在这道闸门前面，
  否则关掉中文之后就没法再把它开回来。
- 切模式会丢掉手上半截的编码，不然切回来它还在，上屏时机就乱。
- 托盘菜单的「英文输入」和中文三档走的是同一个 `SetChineseMode`，所以键盘切完菜单亮英文、
  菜单点回中文档，两边不会各记一套状态。
- 托盘图标跟着换：中文是蓝底「五」(`app.ico`)，英文是琥珀底「A」(`app-en.ico`)，
  停用和英文是两回事，停用时仍显示「五」，只在文字上标「（已停用）」。

## 设置

托盘菜单「设置…」开 `SettingsForm`，配置模型和读写都在 `AppSettings`，文件是 `%APPDATA%\BobIme\settings.json`。

- 界面拿到的是 `Clone()` 出来的副本：「保存」才回写并落盘，「取消」或直接关窗口就丢掉；
  「恢复默认」只是把控件填成 `new AppSettings()`，还得再点一次保存。
- 启动输入档和启动标点档只在 `Program` 起 `ApplyStartupMode` 时读一次，改了要重启才见效；
  其余几项保存后当场接上——`ImeController.Settings` 整个换掉，下一次按键就走新键位。
- 设置窗口开着期间 `LowLevelKeyboardHook.Suspended = true`，钩子照挂但一律放行。
  不这么做的话，录入框要接的那个键会先被钩子吃掉，切中英、切标点档顺手就触发了。
- 键位是 `KeyBinding`（VK + Ctrl/Alt/Shift + 是不是单敲），存成 `ctrl+.`、`tap:lshift` 这种 token。
  裸键不算绑定；单敲只认左右 Ctrl/Alt/Shift 这六个 VK，跟钩子里那套判定共用
  `KeyBinding.IsModifierKey` / `KeyBinding.FromKeyData`，录入框和实际按键行为不会各记一套规则。
- 两个功能绑到同一个键时 `FindToggleConflict()` 拦下保存：中英切换和停用热键撞车，一个键就同时做两件事。
- 翻页键四组可选（`PageKeySet`：`[ ]`、`- =`、`PgUp/PgDn`、`← →`），`Prev()` / `Next()` 给出 VK，
  控制器按键比对；符号面板位置（`PanelPlacement`）决定 `SymbolForm.ShowAt` 走 `Center` 还是 `Locate`；
  托盘双击开面板由 `TrayDoubleClickOpensSymbols` 把关，默认关。
- 每页候选数落在 `ImeEngine.PageSize`（夹 1–9，改档位时当前页夹回可用范围），控制器只是转一手；
  设置界面按 3–9 给，因为选词靠 1–9 数字键。
- 「候选窗位置」三档（`CandidatePlacement`：跟随输入光标 / 跟随鼠标 / 桌面右下角）存进设置，
  保存后交给 `CandidateForm.Placement`；翻页不重新定位，所以改了要等起下一段编码才看得出效果。
  给 `CandidateForm` 这类 Form 派生类新增 public 属性要加
  `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]`，否则 WinForms 源生成器报
  **error WFO1000**，构建直接失败。
- 「按程序定档」是一张表格（程序名 + 档位下拉，档位就是英文档 / 中文档）：「添加一行」插空白行，
  「填成一条规则」取打开设置之前那个前台程序的文件名（读不到就置灰），选中一行按 Del 删除；
  换档的判定见「按程序定档」。表读回的是 `Clone()` 里另起的列表，界面里删一条不会当场改掉正在生效的那份。
- 读文件容错到底：缺文件、坏 JSON、枚举写了不认得的值，一律回默认，`Normalized()` 再夹一遍范围。
  配置文件被手改坏了也只是回到默认档，不会启动失败。

## 候选窗定位

候选窗摆在哪儿由设置里的「候选窗位置」定，三选一：跟随输入光标（默认）、跟随鼠标、桌面右下角。
`CaretLocator.Locate(size, self, placement)` 按档位取锚点，之后一律夹进工作区、再让开置顶窗口。

- 跟随输入光标按四级找位置：系统光标 → UIA 光标 → 鼠标 → 工作区左下。
  系统光标取前台窗口线程的 `GetGUIThreadInfo`；编辑控件挂在另一个线程时，跟着 `hwndFocus` 所属线程再查一次。
  浏览器、Electron 这类程序不建系统光标，第二条路去问 UIA（`UiaCaret`）：焦点元素的 TextPattern 选区
  折叠成光标时给的是 `left/top/width/height` 四元组，取最后一块的左下角，和系统光标同一档物理坐标。
  矩形要落在前台窗口矩形里才算数，否则当脏值丢掉——宁可退回鼠标位，也别把候选窗甩出屏幕。
- 跟随鼠标就是 `GetCursorPos` 往下让 24 像素，不看光标；鼠标停在屏幕边上时照样夹进工作区。
- 桌面右下角取工作区右下角减窗口尺寸再留 8 像素，落在任务栏上方，位置跟编码长短无关。
- 三条路都取不到才跟着鼠标。所以「候选窗跑到鼠标那儿」通常意味着这个程序既不建系统光标、
  焦点元素也没实现 TextPattern，不是取错了位置。
- 设置界面开着时钩子按前台窗口放行（`LowLevelKeyboardHook.CaptureOwner`）。判据不用「对话框开着没」：
  模态框挡不住 Alt+Tab，切到别的程序还照抄放行，就会顺手把那边的输入档、标点档改掉。

- 夹完还要看该点上压着谁：`WindowFromPoint` 拿到窗口、`GetAncestor(GA_ROOT)` 回到根窗口，
  只有它带 `WS_EX_TOPMOST` 且不是我们自己时才算被挡。普通程序不置顶，压不过候选窗，
  所以只有开始菜单这类 shell 窗口会触发。
- 被挡就往边上挪：左、右、下、上取第一个放得下的，最多挪 4 次。
  开始菜单卡片居中，所以实际总是落到左边空白；全屏置顶窗口四边都放不下，就留在原位。
- 这一步不能用「命中的是不是前台窗口」判断：开始菜单的前台窗口本身就是要躲的东西。

## 不抢焦点的浮层

候选窗和符号面板都是 `FloatingForm`：无边框、置顶、不进任务栏、不激活。

- `ShowWithoutActivation` 只挡 `Show()` 那一下，`WS_EX_NOACTIVATE` 只挡标题栏和任务栏那一路；
  客户区的点击要自己回 `WM_MOUSEACTIVATE = MA_NOACTIVATE`，否则点一下箭头就把前台抢走，
  之后的 `SendInput` 全落进自己窗口里，字就没了。这两条都在基类里，别在子类各写一遍。
- 符号面板因此只用鼠标：分组标签换类、格子直接上屏、× 关闭，标题栏可以拖着走。
  它不接收键盘，键盘仍旧归正在打的那个程序，输入法钩子照常工作。

## 跑

```
dotnet build                 # 从仓库根目录，BobIme.slnx
dotnet test
src/BobIme.Shell/bin/Debug/net10.0-windows/BobIme.Shell.exe --check   # 码表/结构体尺寸/钩子/注入通路
src/BobIme.Shell/bin/Debug/net10.0-windows/BobIme.Shell.exe           # 常驻，托盘退出
```

码表路径按 `BOBIME_DICT` / `BOBIME_PINYIN` 环境变量、`--dict=` / `--pinyin=` 参数、
输出目录 `dict\` 里的默认文件这个顺序找。找不到拼音表就退回纯五笔。

`dotnet run --project tools/BobIme.Probe -- -q khlg -q zhongguo` 批量看候选，
不带 `-q` 进交互式敲码。

常驻的 `BobIme.Shell.exe` 会锁住 Debug 输出目录，重建之前先把它结束，重建后按原路径重启。

## 图标与安装包

- 图标源图在 `vibe_images\`，`assets\app.ico` 由工具生成：
  `dotnet run --project tools/BobIme.Icons -- build <源图.png> assets/app.ico 40`。
  那个 40 是往里收的像素数，专门用来把源图右下角的「AI生成」水印裁在框外；
  徽章包围盒按整行整列覆盖率取，不靠肉眼量坐标。
- `app.ico` 同时是 exe 的 `ApplicationIcon`、托盘图标（按 `SM_CXSMICON` 挑档）、
  快捷方式图标和安装包的 `SetupIconFile`。
- `app-en.ico`（英文模式的「A」徽章）只当托盘图标，源图是拿 `app.ico` 那张做图生图、
  只把字换成 A 得到的；`analyze` 量出来没有水印，所以 inset 传 0。
  底色是琥珀/橙的渐变，跟中文那张蓝底分开——两张都是蓝底在 16px 托盘上根本分不出来。
- 出安装包：`bash installer/build.sh`，先自包含发布到 `artifacts\publish`，
  再用 Inno Setup 编 `installer/BobIme.iss`，产物在 `artifacts\installer\`。
  Inno 没自带简体中文语言包，`.iss` 里以英文 `Default.isl` 为底覆写了几条看得见的
  `[Messages]`，所以这个脚本**必须带 UTF-8 BOM**，否则中文按 GBK 解读就花了。
- 版本号写在两处、必须一起改：`src/BobIme.Shell/BobIme.Shell.csproj` 的 `<Version>` 和
  `installer/BobIme.iss` 的 `#define MyAppVersion`。发布产物的 `ProductVersion` 形如
  `1.1.1+<完整 git sha>`，SDK 从提交号拼出来，托盘「关于」显示的就是它，所以**先提交、再 publish**，
  否则包里带的源码号是上一次提交。
- `.github/workflows/release.yml` 就是这套流程的自动化：推 `v*` 标签 → 核对两处版本号与标签一致 →
  `dotnet test` → 装 Inno Setup → `installer/build.sh` → 跑一次 `--check` → 建/更新 Release 并传安装包。
- `.github/workflows/ci.yml` 只管推 main 和 PR 时跑一遍 `dotnet test`，不出包也不发版，
  README 顶上那枚构建徽章指的就是它；发版徽章走 `v*` 标签那条。两条别合一条，
  合了以后每次推 main 都会去核对版本号、装 Inno，白等三十秒还可能误建 Release。
- **winget 清单在 `installer\winget\manifests\b\bobvan9\BobIme\<版本>\`**，四个文件（version/installer/
  defaultLocale/locale），目录层级照上游 `manifests/<小写首字母>/<Publisher>/<App>/<Version>/`，整份版本目录
  拷进 fork 就能提 PR。跟着版本走的是三处：`PackageVersion`、`InstallerUrl` 里的 `v<版本>` 和文件名、
  `InstallerSha256`；哈希别自己算，取 Release 附件的 digest：
  `gh api repos/bobvan9/BobIme/releases/latest --jq '.assets[]|.digest'`。
  自检用本机 winget：`winget validate --manifest <那个目录>`（当前 schema 是 1.28.0，`Platform` 是枚举表，
  写「Windows 10」这种自由文本会被判不合格，索性不写）。
  上游流水线里 **08 Installation Validation 已于 2026-11-22 过**（他们在 VM 里真静默装了一遍），
  等于把 Silent 开关、machine 作用域和 `ProductCode` 识别一起验了，别再回头猜要不要加 `InstallerSuccessExitCodes`。
  `ProductCode` 填的是 `.iss` 里 `AppId` 那个 GUID，实测装机键就是
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7A3C1E58-…}_is1`——**AppId 改了升级识别就断**，
  这条跟 `.iss` 顶上那句注释是同一件事。安装包只有 win-x64 一个产物，清单里就只挂 x64，别给 arm64 凑一条。
- **发版时那份清单由 `installer\winget-submit.sh` 自动出**：`release.yml` 建完 Release 多一步调它，脚本拿上一版目录当模板
  生成新版本目录（临时目录里生成，不碰工作树），填 `PackageVersion`、直链里的 `v<版本>` 与文件名、digest 转大写的
  `InstallerSha256`、Release 的 `published_at` 当 `ReleaseDate`，然后用 GitHub API 建 fork、开分支、四份文件 PUT 上去、
  开 PR，最后把同样四份回写本仓库 main。要的东西：一个 **`WINGET_PAT` secret**（能推自己的 `winget-pkgs` fork 并在
  `microsoft/winget-pkgs` 开 PR，classic PAT 给 `repo` 最省事），没配就打印生成结果跳过提交，发版本身仍旧成功。
  上游默认分支是 `master`，别写 `main`；标题按上游有没有这个包目录分「New package:」/「Update: … to …」两种。
  分支名是 `submit-<包>-<版本>`，重跑同一个 tag 时 `gh pr create` 会撞已存在的 PR，脚本认那条旧的、照旧退 0，
  别让它把整个发版判失败。**手动提交的那次分支叫 `new-bobvan9.BobIme-1.1.3`**，跟脚本的命名不同，
  所以万一要重跑 1.1.3 得留意上游会拦第二个同版本 PR。
  本地空跑：`WINGET_OUT=<临时目录> WINGET_SHA=<64 个字符> WINGET_DATE=2026-01-01 bash installer\winget-submit.sh 1.9.9`，
  带这三个变量就不会去碰网络。**想单验令牌**就跑 Actions 里的「验 winget 令牌」（`.github\workflows\winget-check.yml`，
  它只多设一个 `WINGET_CHECK=1`，脚本在验完账号和 fork 写权限之后就退，建分支/PUT/开 PR/回写一步都不走）；
  版本号填一个已经发过版的，因为填哈希要从那个 Release 附件读 digest。`sed` 填哈希那条别拿 `^` 锚——`InstallerSha256` 在 `Installers:` 条目下面是缩进的，
  锚死了静默不替换，脚本末尾加了一条 grep 兜底。
  取哈希那两条 `gh api` 拿 1.1.3 真跑过一遍，生成结果和仓库里那份副本逐字节一致；没配 `WINGET_PAT` 时脚本只打印跳过、
  退 0，发版照旧成功。脚本本身在 `.gitattributes` 里是 `*.sh eol=lf`，别让它带 CRLF，否则 runner 上 bash 会当场崩。
- 现在是 `PrivilegesRequired=admin` 的全用户安装，默认目录 `C:\Program Files\BobIme`（要写系统目录，
  所以安装和升级都会弹一次 UAC）；开机自启写 HKLM 的 `Run` 项，对所有登录用户生效。
  安装时会顺手删掉早先按用户安装留下的 HKCU `Run` 项，免得登录后拉起两份进程挂两个钩子。

## 为什么不走系统输入法（TSF）

2026-11-20 走到 B1 就停了，`src\BobIme.Tip`、`tools\BobIme.TipRegistrar`、`docs\tsf-plan.md`
都删了（仓库外留了一份 `..\BobIme-tsf-backup.tar.gz`），结论记在这儿，别重复做一遍：

- .NET 的 `EnableComHosting` comhost 确实能在宿主进程里起 CoreCLR——从 CLR4 的 PowerShell 5.1
  就把对象建出来了，宿主进程名落进了构造日志。这条路本身没问题。
- TIP 名单 msctf 只从 **HKLM** 读：只写 HKCU 的 COM 注册对 COM 够用，但不提权时
  `Register`/`AddLanguageProfile`/`RegisterCategory` 一律 `E_FAIL 0x80004005`，提权后四个调用全 `S_OK`。
  接口 IID 和虚表顺序得从 `msctf.h` 抄（MS Learn 不发布这些），CLSID 可以从注册表现成条目反查。
- 登记完语言栏下拉里确实出现「Bob 五笔」，描述和注册表形状都跟微软自己的 TIP 一致，
  `EnumLanguageProfiles(0x0804)` 也列得出它——**但点它切不过去**，构造日志一条没写，
  说明 msctf 压根没实例化我们的 TIP。卡点在 Win11 的切换路径，不在 comhost、也不在 COM 注册。
  没试完的那步：改用 Vista+ 的 `ITfInputProcessorProfileMgr::RegisterProfile`
  （`TF_PROFILETYPE_INPUTPROCESSOR = 1`，虚表槽 8）而不是 `AddLanguageProfile`。
- 全屏游戏不是走 TSF 的理由：独占全屏那块 surface 任何外部窗口都画不上去，微软拼音能行是因为
  那些游戏自己集成了输入法。真要照顾游戏，方向是无边框窗口模式，再不行加剪贴板粘贴上屏兜底。

## 已知问题

- **记事本里打过一个标点，之后上屏的中文显示成空心方框。** 字是好的：关掉记事本再打开，
  同一批字就变回中文，没有错字。所以是记事本自己的渲染问题——它那个编辑控件在「物理键打标点、
  紧接着由注入上屏中文」之后没有重算字体回退。Word 和 Qoder 走各自的文本渲染，不受影响。
  别再往码表、代理对、`SendInput` 通路上找，那边查过都是对的。
- 提权窗口里收不到键：低级钩子被 UIPI 挡住，管理员权限运行的程序得另想办法。
- **默认输入语言得是「英语(美国)」**，装完先改（README「安装」第 2 步）。两条原因：编码字符按物理键位取
  （`(char)('a' + key.Vk - VirtualKey.A)`，`src/BobIme.Shell/ImeController.cs:172`），换成非 QWERTY 布局码就乱了；
  默认布局留给微软拼音那类系统输入法，它会和钩子两边同时组字。

## 验证的手法和边界

- 低级钩子会跳过注入按键（`LLKHF_INJECTED`），所以端到端打字没法自己测——能测的是几何和纯逻辑。
  逻辑一律收进 `public` 的纯函数走单测；窗体要看长相就用一次性反射工程：`%TEMP%` 下建个 console 项目，
  `Assembly.LoadFrom` 载入 Debug 的 DLL，`form.DrawToBitmap` 存图。别用截屏，窗口被别的东西压着就拍到别人了，
  `DrawToBitmap` 也不用抢前台。
- `--check` 会打印码表条数、三档候选窗位置坐标、钩子安装和 SendInput 回读，是 Release 产物最便宜的验证。
