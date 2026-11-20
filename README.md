# Bob 五笔

自己用的 Windows 五笔输入法，走「码表直查」路线：不做字根拆字，编码到词条全靠码表前缀查询。
输入方式 = 全局低级键盘钩子 + 置顶候选窗 + SendInput 上屏，不是 TSF 系统输入法，
所以切到它之后要在托盘里确认状态，Ctrl+Space 开关、左 Shift 单敲切中/英。

## 目录

- `src/BobIme.Core` 码表加载、前缀查询、组码状态机。不碰窗口也不碰键盘，可以单测。
- `src/BobIme.Shell` 钩子、候选窗、光标定位、SendInput 注入、托盘常驻。
- `tools/BobIme.Probe` 命令行敲码工具，调编码、看候选顺序用。
- `data/dict` 两份码表，见下。

## 码表

| 文件 | 来源 | 列 | 说明 |
| --- | --- | --- | --- |
| `jisuan43.tsv` | 极爽词库 4.3 修订版 | `词\t码` | 87521 条，五笔最长 4 码 |
| `pinyin_base.tsv` | rime-ice `base` + `8105` + `41448` | `词\t拼音\t词频` | 395529 条，拼音连写最长 16 字母 |

两份都不入库前先过一遍规则：

- 挂在编码上的中文标点、声调符号、私用区字符一律不加载。本产品全程用英文标点，
  这些词条只会变成空白候选，还白占 `cobd`、`nhty`、`xyxy` 这类正常编码位。
- 单个拉丁字母词条也不加载：`ā á ǎ à a` 整块挂在 `copa`、`copu` 上，
  `cosx` 上只留一个 `π`。
- 日语假名整块不加载：平假名挂在 `cojp`、片假名挂在 `copj`。
- `cop*` 那一块剩下的就是 `勹 灬 冫 艹 屮` 这类部首字，是要打生僻字的底子，暂时留着。
- 拼音三张源表合并成一个 TSV：音节列去掉空格、带词频列但词频低于 100 的丢掉、
  词长超过 8 字或码长超过 16 字母的丢掉，同形同码只留词频高的那条。
  重新生成用 `perl tools/conv_pinyin.pl <输出> <源表...>`。

## 混输规则

一次按键同时查两套码表，候选按「五笔段在前、拼音段在后」拼成一页：

- 每套码表先保底分到半页（`PageSize` 9 → 各 5 / 4），没用完的名额顺延给下一套，
  免得五笔的上千条同前缀词把拼音挤没。
- 五笔表按码长分带二分查，简码先出；拼音表按词频降序线性扫，
  扫到 60000 条封顶，所以 `sh` 出的是「是说上时」而不是「啥傻杀刹」。
- 编码超过 4 个字母后五笔表自然无解，这一段就只剩拼音候选。
- 唯一全码命中且整页只有一条时才自动上屏，拼音段还有候选就不抢；但四码不算，
  四码一律等空格确认（`AutoCommitCodeLimit`）。
- 候选条上不给拼音段写标签：整段降成次要灰、前面点一个间隔点，比「｜拼音」两个字安静。
  两段是分开两次画的，拼音段的起点 x 就是五笔段量出来的宽度。

## 大写英文

大写字母一律不当编码，原样交给目标程序：

- 按住 Shift 敲的字母由钩子里自己记录的修饰键状态判定，直接放行。
- Caps Lock 开着敲出来的也是大写，同样放行。这个状态每次按键现读
  `GetKeyState(VK_CAPITAL)`，不自己维护影子状态，切灯之后立刻生效。
- 放行时不清编码缓冲，所以不会把他还没挑完的词顺手顶上屏；
  而按下 Caps Lock 那一下属于未指派按键，会先把当前首选词上屏，
  于是「中CPU」这类混排的顺序是自然的。

## 中/英切换

左 Shift 单敲切中/英，和系统输入法的手感一致：

- 「单敲」的判定在钩子里记账：左 Shift 按下后，抬起之前只要碰过任何一个别的键就不算，
  于是 Shift 打大写字母、Ctrl+Shift 切布局都不会误触。判定只看左 Shift 的抬起，
  右 Shift 单敲不切。
- 记账和回调都跑在钩子回调里，所以切模式这件事是 `Post` 回消息循环做的，回调本身只投递。
- 英文模式一个键都不吃，全交回目标程序；`Ctrl+Space` 的判断抢在这道闸门前面，
  否则关掉中文之后就没法再把它开回来。
- 切模式会丢掉手上半截的编码，不然切回来它还在，上屏时机就乱。
- 托盘图标跟着换：中文是蓝底「五」(`app.ico`)，英文是琥珀底「A」(`app-en.ico`)，
  停用和英文是两回事，停用时仍显示「五」，只在文字上标「（已停用）」。

## 候选窗定位

`CaretLocator.Locate` 按三级找位置：光标 → 鼠标 → 工作区左下，然后一律夹进工作区。

- 夹完还要看该点上压着谁：`WindowFromPoint` 拿到窗口、`GetAncestor(GA_ROOT)` 回到根窗口，
  只有它带 `WS_EX_TOPMOST` 且不是我们自己时才算被挡。普通程序不置顶，压不过候选窗，
  所以只有开始菜单这类 shell 窗口会触发。
- 被挡就往边上挪：左、右、下、上取第一个放得下的，最多挪 4 次。
  开始菜单卡片居中，所以实际总是落到左边空白；全屏置顶窗口四边都放不下，就留在原位。
- 这一步不能用「命中的是不是前台窗口」判断：开始菜单的前台窗口本身就是要躲的东西。

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
- 现在是 `PrivilegesRequired=lowest` 的按用户安装：不弹 UAC，开机自启写当前用户的 `Run` 项。

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
