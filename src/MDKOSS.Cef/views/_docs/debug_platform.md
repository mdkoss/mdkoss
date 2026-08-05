# 骞冲彴璁惧璋冭瘯椤?鈥?姝ヨ繘绀烘暀锛堣璁¤鏄庯級

> **鐩爣椤甸潰**锛歚debug_platform.html`锛堝疄鐜扮锛? 
> **鏈枃妗?*锛歚_docs/debug_platform.md`  
> **瀵规爣鍙傝€?*锛氭満鍣ㄤ汉绠＄悊鍣?鈫掋€屾杩涚ず鏁欍€嶇晫闈紙瑙侀」鐩?`assets/` 涓弬鑰冩埅鍥撅級  
> **璋冭瘯瀵硅薄**锛氳繍琛屾椂 `PlatformDevice`锛堝杞村钩鍙帮紝`MPlatformKind`锛歑Y / XYZ / XYZU / XYZUV / XYZUVW锛?
---

## 1. 椤甸潰瀹氫綅

| 椤?| 璇存槑 |
|---|---|
| 鍚嶇О | **骞冲彴姝ヨ繘绀烘暀**锛圥latform Jog / Step Teach锛?|
| 鐢ㄩ€?| 鍦ㄧ洃鎺?HTTP 鏈嶅姟涓嬶紝瀵瑰崟涓?`PlatformDevice` 鍋氳仈璋冿細鏌ョ湅鍚勮酱浣嶇疆銆佹杩?杩炵画鐐瑰姩銆佷娇鑳?鍘讳娇鑳姐€佷繚瀛?鍥炴斁绀烘暀鐐癸紙浜屾湡锛?|
| 鍏ュ彛 | `GET /debug_platform.html?deviceId={platformId}`锛涙棤鍙傛暟鏃朵粠 `GET /api/devices` 绛涢€?`type` 涓?`platform` / `xy` / `xyz` 鈥?鐨勮澶囧垪琛?|
| 椋庢牸 | 涓?`debugserialdev.html`銆乣monitor_runtime.html` 涓€鑷达細娣辫壊闈㈡澘銆佸崱鐗囧垎鍖恒€?s 杞鐘舵€?|
| 闈炵洰鏍?| 涓嶅疄鐜版満姊拌噦閫嗚В銆丠and/Elbow/Wrist 绛?SCARA 涓撴湁濮挎€侊紙鍙傝€冨浘鍙充晶銆屾墜鑷傛柟鍚戙€嶅湪骞冲彴椤典腑鏇挎崲涓恒€屽钩鍙?杞寸姸鎬併€嶏級 |

---

## 2. 鎬讳綋甯冨眬

鍙傝€冪ず鏁欑晫闈紝閲囩敤 **宸﹀姩鍙虫樉銆佸簳鏍忔墿灞?* 涓夋爮缁撴瀯锛堝灞?鈮?200px锛夛細

```
鈹屸攢鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹?鈹?鏍囬锛氬钩鍙版杩涚ず鏁?路 {platformName} ({platformId})     [鍒锋柊] [杩斿洖鐩戞帶棣栭〉] 鈹?鈹溾攢鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹攢鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹?鈹?鈶?姝ヨ繘鎺у埗锛堝乏锛岀害 38%锛?     鈹?鈶?鐘舵€佷笌姝ヨ窛锛堝彸锛岀害 62%锛?                   鈹?鈹? 路 妯″紡 / 閫熷害               鈹? A. 鐩墠浣嶇疆                                  鈹?鈹? 路 杞寸偣鍔ㄦ寜閽煩闃?            鈹? B. 骞冲彴涓庤酱鐘舵€?                             鈹?鈹? 路 骞冲彴浣胯兘鏉?                鈹? C. 姝ヨ繘璺濈                                  鈹?鈹溾攢鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹粹攢鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹?鈹?鈶?搴曟爮閫夐」鍗★細绀烘暀鐐?| 鎵ц鍔ㄤ綔 | 鍏宠仈 IO锛堝彲閫夛級                              鈹?鈹斺攢鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹?```

绐勫睆锛?lt;1024px锛夛細涓娾啋涓嬪爢鍙犱负 鈶?鈫?鈶?鈫?鈶€?
---

## 3. 鍖哄煙瑙勬牸

### 3.1 椤舵爮

| 鎺т欢 | 绫诲瀷 | 琛屼负 |
|---|---|---|
| 骞冲彴閫夋嫨 | `<select>` | 鏁版嵁婧愶細`GET /api/devices`锛屼粎 `type` 鈭?`platform, xy, xyz, xyzu, xyzuv, xyzuvw`锛涘彉鏇村悗閲嶈浇杞存寜閽笌浣嶇疆鍖?|
| 杩愯鎽樿 | 鏂囨湰 + 寰界珷 | `GET /api/status` 鈫?`isRunning`銆侀」鐩悕锛涘钩鍙?`state`銆佸悇杞?`driverConnected`锛堟潵鑷?`device.platformAxes`锛?|
| 鍒锋柊闂撮殧 | 鍙鏍囩 | 榛樿 **1s** 杞浣嶇疆锛涚偣鍔ㄦ寜涓嬫椂 **100ms** 鍔犻€熷埛鏂帮紙鏉惧紑鎭㈠锛?|
| 杩斿洖 | 閾炬帴 | `/` 鎴?`monitor_runtime.html` |

---

### 3.2 鈶?姝ヨ繘鎺у埗锛堝鏍囧弬鑰冨浘宸︿晶銆屾杩涖€嶏級

#### 3.2.1 妯″紡 / 閫熷害

| 鎺т欢 | 閫夐」 | 鏄犲皠锛堜竴鏈燂級 | 鏄犲皠锛堜簩鏈燂紝闇€ runtime锛?|
|---|---|---|---|
| 妯″紡 (O) | 榛樿 / 鍏宠妭 / 鐩稿涓栫晫 | UI 鐘舵€侊紱涓€鏈熶粎銆岄粯璁ゃ€嶇敓鏁?| `coordinateMode`: `world` \| `joint` |
| 閫熷害 (D) | 浣?/ 涓?/ 楂?| 鏄犲皠鐐瑰姩 `velocity` 鍊嶇巼锛?.25 / 0.5 / 1.0 | 鍐欏叆 vars 鎴?action 鍙傛暟 |

#### 3.2.2 杞寸偣鍔ㄦ寜閽煩闃?
鎸?`MPlatformKind` **鍔ㄦ€佺敓鎴?*锛屼粎鏄剧ず褰撳墠骞冲彴鎷ユ湁鐨勮酱瀛楁瘝锛?
| Kind | 鍙敤杞?| 鍗曚綅锛堟樉绀猴級 |
|---|---|---|
| Xy | X, Y | X/Y锛歮m锛堟垨閰嶇疆鍗曚綅锛?|
| Xyz | X, Y, Z | Z锛歮m |
| XyzU | + U | U锛歞eg |
| XyzUv | + V | V锛歞eg |
| XyzUvw | + W | W锛歞eg |

**甯冨眬**锛堜笁鍒楃綉鏍硷紝涓庡弬鑰冨浘涓€鑷达紱鏃犺酱鐨勬牸瀛愪笉娓叉煋锛夛細

```
鍒?          鍒?          鍒?
+X  -X       -Y  +Y       +Z  -Z
+Y  -Y       -V  +V       -W  +W   锛堜粎 xyzuv / xyzuvw锛?-U  +U       +S  -S       +T  -T   锛圡DKOSS 鏃?R/S/T 杞达紝涓嶆樉绀猴級
```

- 鎸夐挳鏍囩锛歚+X`銆乣-X` 鈥︼紱鍥炬爣鍙敤 Unicode 绠ご鎴?SVG锛屼富鑹?`--accent`锛岀鐢ㄦ€?`--muted`銆?- **鎸変笅**锛歚mousedown` / `touchstart` 寮€濮嬬偣鍔紱`mouseup` / `mouseleave` / `touchend` 鍋滄銆?- **绂佺敤鏉′欢**锛氬钩鍙版垨璇ヨ酱 `driverOnline === false`锛涙湭 `enable` 涓旈┍鍔ㄨ姹備娇鑳斤紙濡?`drvsim`锛夛紱`runtime.isRunning === false` 鏃舵樉绀鸿鍛婃潯浣嗕粛鍏佽璋冭瘯锛堜笌涓插彛椤典竴鑷达紝鍙厤缃級銆?
#### 3.2.3 鐐瑰姩鍔ㄤ綔锛堜竴鏈熷疄鐜拌矾寰勶級

骞冲彴瀛愯酱鍦ㄨ繍琛屾椂 ID 涓?`{platformId}.{letter}`锛堜緥锛歚dev-platform-kind-xyz.X`锛夈€?
| 姝ヨ繘妯″紡 | HTTP 璋冪敤 |
|---|---|
| 姝ヨ繘锛堢鏁ｏ級 | `POST /api/devices/{platformId}.{letter}/action` body: `{ "action": "move", "parameters": { "position": current + sign * step } }` |
| 杩炵画锛堟寜浣忥級 | 鍚屼笂锛屾瘡 **80鈥?20ms** 閲嶅锛涙垨浜屾湡 `action: "jog"` + `direction` + `stopOnRelease` |

`current` 浠?vars 瑙ｆ瀽锛岄敭鍚嶈鍒欙細

```text
device.{axisDeviceName}.{axisDeviceId}.position
```

渚嬶細`device.Platform kind=xyz.dev-platform-kind-xyz.X.position`

**骞冲彴绾т娇鑳?*锛堟寜閽潯锛夛細

```http
POST /api/devices/{platformId}/action
{ "action": "enable" }   // PlatformDevice.SetMotion(true)
{ "action": "disable" }
```

鍗曡酱浣胯兘锛堝彲閫夛紝楂樼骇锛夛細

```http
POST /api/devices/{platformId}.{letter}/action
{ "action": "enable" | "disable" }
```

---

### 3.3 鈶?鐘舵€佷笌姝ヨ窛锛堝鏍囧弬鑰冨浘鍙充晶锛?
#### A. 鐩墠浣嶇疆

| 瀛楁 | 鎺т欢 | 鏁版嵁婧?|
|---|---|---|
| X, Y, Z | 鍙鏁板€兼锛? 浣嶅皬鏁?| 瀵瑰簲瀛愯酱 `*.position` var |
| U, V, W | 鍚屼笂锛涘钩鍙版棤璇ヨ酱鏃?**鐏版樉鍗犱綅** `--` | 鍚屽乏 |
| 鍧愭爣绯?| 鍗曢€夛細涓栫晫 (W) / 鍏宠妭 (J) / 鑴夊啿 (U) | 涓€鏈燂細**鍏宠妭**=鍚勮酱鐙珛璇绘暟锛?*涓栫晫**=鍚屽叧鑺傦紙鏃?FK锛夛紱**鑴夊啿**=鍙鏄剧ず椹卞姩鍘熷鍊硷紙鑻?var 瀛樺湪锛?|

杞锛歚GET /api/status` 鈫?`vars` 杩囨护褰撳墠 `platformId` 涓嬫墍鏈?`*.position`銆?
#### B. 骞冲彴涓庤酱鐘舵€侊紙鏇夸唬鍙傝€冨浘銆岀洰鍓嶇殑鎵嬭噦鏂瑰悜銆嶏級

| 鍧?| 鍐呭 |
|---|---|
| 骞冲彴 | `platformKind`锛坸y/xyz/鈥︼級銆乣motionEnabled`銆乣state` |
| 杞磋〃 | 鍒楋細杞淬€佸瓙璁惧 ID銆乨riverId銆侀┍鍔ㄥ湪绾裤€佷娇鑳姐€佹渶鍚庨敊璇紙鑻ユ湁 `*.error` var锛?|
| 璇︽儏 | `GET /api/devices/{platformId}` 鈫?`platformAxes[]` |

涓嶅睍绀?Hand / Elbow / Wrist锛涜嫢鍚庣画鎺?SCARA锛屽彲鍦ㄦ鍖哄鍔犳姌鍙犻潰鏉裤€屾満姊拌噦濮挎€侊紙鎵╁睍锛夈€嶃€?
#### C. 姝ヨ繘璺濈

| 鎺т欢 | 璇存槑 |
|---|---|
| 姣忚酱杈撳叆 X鈥 | 鏁板瓧妗嗭紱鍗曚綅涓庤酱涓€鑷达紱榛樿瑙佷笅琛?|
| 棰勮鍗曢€?| **杩炵画 (C)**锛氭寜浣忓嵆鍔紝涓嶆寜姝ヨ窛绱姞锛?*闀?(L) / 涓?(M) / 鐭?(S)**锛氫竴閿～鍏ュ悇杞存璺?|

榛樿姝ヨ窛寤鸿锛堝彲 localStorage 璁板繂锛夛細

| 棰勮 | 鐩寸嚎杞?(mm) | 鏃嬭浆杞?(deg) |
|---|---|---|
| 闀?L | 10 | 5 |
| 涓?M | 1 | 1 |
| 鐭?S | 0.1 | 0.1 |

杩炵画妯″紡涓嬫璺濊緭鍏ュ彧璇伙紱姝ヨ繘妯″紡涓嬫瘡娆＄偣鍑?卤 鎸夐挳浣跨敤瀵瑰簲杞存璺濄€?
---

### 3.4 鈶?搴曟爮閫夐」鍗?
#### Tab 1锛氱ず鏁欑偣锛堝鏍囥€岀ず鏁欑偣銆嶏級

| 鎺т欢 | 琛屼负 |
|---|---|
| 鐐规枃浠?(P) | 涓嬫媺锛歚localStorage` 鎴?`configs/teach/{platformId}.json`锛堜簩鏈熸湇鍔＄锛?|
| 鐐?(P) | 鍒楄〃 P0鈥n锛涙樉绀哄悕绉颁笌鏄惁宸插畾涔?|
| 绀烘暀 (T) | 灏嗗綋鍓嶅悇杞?`position` 鍐欏叆閫変腑鐐?|
| 瀹氫綅 / 杩愯 | 瀵规墍鏈夎酱渚濇 `move` 鍒拌褰曚綅缃紙闇€鍏?enable锛?|
| 閫€鍑?(E) | 鍏抽棴椤垫垨杩斿洖鐩戞帶棣栭〉 |

**鐐规暟鎹粨鏋勶紙JSON锛?*锛?
```json
{
  "platformId": "dev-platform-kind-xyz",
  "kind": "xyz",
  "points": [
    { "id": "P0", "name": "Home", "axes": { "X": 0, "Y": 0, "Z": 0 } }
  ]
}
```

涓€鏈燂細浠呮祻瑙堝櫒 `localStorage` + 瀵煎嚭/瀵煎叆 JSON 鏂囦欢锛涗笉鍐欏洖 `sample.setting.json`銆?
#### Tab 2锛氭墽琛屽姩浣?
| 椤?| 璇存槑 |
|---|---|
| 骞冲彴鍔ㄤ綔 | enable / disable锛堝悓涓婏級 |
| 鑷畾涔?| 鏂囨湰妗嗚緭鍏?`action` + JSON `parameters`锛宍POST .../action`锛堣皟璇?API锛?|
| 鏃ュ織 | 鏄剧ず鏈€杩戜竴娆¤姹?鍝嶅簲锛堜笌涓插彛璋冭瘯椤垫敹鍙戝尯绫讳技锛?|

#### Tab 3锛氬叧鑱?IO锛堝彲閫夛級

浠?`GET /api/status` 鍒楀嚭鍚岄」鐩?`gpio` / `vio` 璁惧锛屽彧璇荤洃瑙?+ 蹇嵎璺宠浆 `monitor_io.html`锛涗笉鍦ㄦ椤电洿鎺ュ啓 DO锛堥伩鍏嶈瑙︼級銆?
---

## 4. 涓庡弬鑰冪晫闈㈢殑宸紓瀵圭収

| 鍙傝€冨浘锛堟満鍣ㄤ汉绠＄悊鍣級 | 鏈钩鍙伴〉锛圥latformDevice锛?|
|---|---|
| 杞?R, S, T | **涓嶆樉绀?*锛坄MPlatformKind` 鏃犳杞达級 |
| 鎵嬭噦鏂瑰悜 Hand/Elbow/Wrist | **骞冲彴涓庤酱鐘舵€?*琛?|
| 涓栫晫/鍏宠妭/鑴夊啿 | 淇濈暀 UI锛涗竴鏈熷叧鑺?鍒嗚酱浣嶇疆锛屼笘鐣?鑴夊啿涓哄崰浣嶆垨鍙鎵╁睍 |
| 绀烘暀鐐?`.pts` 鏂囦欢 | JSON + localStorage锛堝懡鍚嶅彲 `.pts.json`锛?|
| 澶瑰叿 Tab | 涓夋湡鎴栭摼鎺ュ埌澶栬 GPIO 椤?|

---

## 5. HTTP / 杩愯鏃朵緷璧?
### 5.1 宸叉湁 API锛堢洿鎺ュ彲鐢級

| 鏂规硶 | 璺緞 | 鐢ㄩ€?|
|---|---|---|
| GET | `/api/status` | 杞 `vars`銆乣devices`銆乣isRunning` |
| GET | `/api/devices` | 骞冲彴鍒楄〃 |
| GET | `/api/devices/{id}` | `platformAxes`銆佺姸鎬?|
| POST | `/api/devices/{id}/action` | `enable` / `disable` / `move`锛堣酱璁惧锛?|

### 5.2 寤鸿鎵╁睍锛堜簩鏈燂紝鍐欏叆 `MdkRuntime`锛?
| action | device | parameters | 璇存槑 |
|---|---|---|---|
| `jog` | `{platformId}.{letter}` | `direction`: 卤1, `mode`: `step`\|`continuous`, `step?`, `velocity?` | 鎸変綇杩炵画銆佹澗寮€鍋滄 |
| `jogStop` | 鍚屼笂 | 鈥?| 鍋滄褰撳墠杞?|
| `readPositions` | `{platformId}` | 鈥?| 杩斿洖 `{ "X": 1.2, ... }` 鑱氬悎锛屽噺灏戝墠绔В鏋?vars |
| `teach` / `gotoPoint` | `{platformId}` | `pointId`, `file?` | 鏈嶅姟绔ず鏁欑偣锛堝彲閫夛級 |

鏂囨。鍖栨椂涓€鏈熷墠绔?*涓嶅緱鍋囪**浜屾湡 API 宸插瓨鍦紱搴旂敤 `move` + vars 瑙ｆ瀽瀹屾垚 MVP銆?
---

## 6. 鍓嶇瀹炵幇瑕佺偣锛坄debug_platform.html`锛?
1. **CSS 鍙橀噺**锛氬鐢?`debugserialdev.html` 鐨?`:root` 鑹叉澘涓?`.card` / `.btn` / `.form-group`銆?2. **璁惧鍙戠幇**锛氬惎鍔ㄦ椂 `fetch('/api/devices')` 鈫?杩囨护骞冲彴鏃?鈫?鑻?URL 甯?`deviceId` 鍒欓€変腑銆?3. **杞存寜閽敓鎴?*锛氭牴鎹€変腑璁惧鐨?`driverType` 鎴栬鎯呮帴鍙ｈ繑鍥炵殑 `platformAxes.length` 涓?kind 鏋氫妇鐢熸垚鐭╅樀锛坘ind 鍙粠 vars `device.*.{platformId}.platformKind` 璇诲彇锛夈€?4. **闃叉姈**锛氳繛缁偣鍔ㄤ娇鐢?`requestAnimationFrame` 鎴?`setInterval(100)`锛屾澗寮€蹇呴』 `clearInterval` 骞跺彲閫夊彂 `disable`锛堜粎褰撳疄鐜?jogStop锛夈€?5. **閿欒鎻愮ず**锛歚action` 澶辫触鏃堕《閮?toast锛歚error` 瀛楁 + 杞村悕銆?6. **鏃犻殰纰?*锛氭寜閽?`aria-label` 涓恒€孹 杞存鍚戞杩涖€嶏紱閿洏涓嶆敮鎸佽繛缁寜浣忔椂鍙敼涓哄崟鍑绘杩涖€?
### 6.1 璺敱娉ㄥ唽锛圕#锛?
涓?`DebugSerialDevPage` 鍚岀骇鏂板锛?
- `src/MDKOSS.Core/server/monitorplatformpage.cs` 鈫?璇诲彇 `views/debug_platform.html`
- `src/MDKOSS.Core/server/monitoringserver.cs`锛歚GET /debug_platform.html` 杩斿洖璇?HTML

`MDKOSS.Cef/MDKOSS.Cef.csproj` 宸插寘鍚?`views/**/*` 澶嶅埗瑙勫垯锛屾棤闇€鏀?csproj銆?
---

## 7. 鑱旇皟妫€鏌ユ竻鍗?
- [ ] `sample.setting.json` 涓嚦灏戜竴涓?`platform` / `xyz` 璁惧宸?`enabled`
- [ ] 瀵瑰簲杞撮┍鍔?`drv-sim` 鎴?`drv-main` 宸茶繛鎺?- [ ] 鎵撳紑 `http://127.0.0.1:5080/debug_platform.html?deviceId=dev-platform-kind-xyz`
- [ ] 浣胯兘鍚庡崟杞?卤 姝ヨ繘锛宍vars` 涓?`position` 鍙樺寲
- [ ] `xyzuvw` 璁惧鏄剧ず 6 杞存寜閽紱`xy` 浠?4 涓紙卤X 卤Y锛?- [ ] 椹卞姩绂荤嚎鏃舵寜閽鐢ㄣ€佺姸鎬佸尯绾㈣壊鎻愮ず
- [ ] 绀烘暀鐐逛繚瀛?瀵煎嚭 JSON 鍐嶅鍏ュ彲鎭㈠

---

## 8. 绾挎锛圡VP锛?
```mermaid
flowchart TB
  subgraph Header[椤舵爮]
    Sel[骞冲彴閫夋嫨]
    Run[杩愯/杩炴帴鐘舵€乚
  end
  subgraph Left[姝ヨ繘鎺у埗]
    Mode[妯″紡/閫熷害]
    Grid[杞存寜閽煩闃礭
    En[骞冲彴浣胯兘 Enable/Disable]
  end
  subgraph Right[鐘舵€佷笌姝ヨ窛]
    Pos[鐩墠浣嶇疆 X-Y-Z-U-V-W]
    Sta[骞冲彴涓庤酱鐘舵€佽〃]
    Step[姝ヨ窛 + 杩炵画/闀?涓?鐭璢
  end
  subgraph Bottom[搴曟爮 Tab]
    T1[绀烘暀鐐筣
    T2[鎵ц鍔ㄤ綔]
    T3[鍏宠仈 IO]
  end
  Header --> Left
  Header --> Right
  Left --> Bottom
  Right --> Bottom
```

---

## 9. 鏂囦欢娓呭崟

| 鏂囦欢 | 瑙掕壊 |
|---|---|
| `src/MDKOSS.Cef/views/_docs/debug_platform.md` | 鏈璁¤鏄?|
| `src/MDKOSS.Cef/views/debug_platform.html` | 椤甸潰瀹炵幇锛堝緟寮€鍙戯級 |
| `src/MDKOSS.Core/server/monitorplatformpage.cs` | 闈欐€侀〉鍔犺浇鍣紙寰呭紑鍙戯級 |
| `src/MDKOSS.Core/core/mdev.cs` | `PlatformDevice` / `MPlatformKind` |
| `src/MDKOSS.Core/core/mdk.cs` | `ExecuteDeviceAction` |
| `src/MDKOSS.Core/server/monitoringserver.cs` | HTTP 璺敱 |

---

## 10. 鐗堟湰瑙勫垝

| 鐗堟湰 | 鑼冨洿 |
|---|---|
| **MVP** | 甯冨眬 + 骞冲彴閫夋嫨 + 浣嶇疆杞 + 姝ヨ繘鎸夐挳 + 骞冲彴 enable/disable + localStorage 绀烘暀鐐?|
| **v1.1** | `jog` / `jogStop`銆佽仛鍚?`readPositions`銆丟TS/浠跨湡閫熷害鏇茬嚎 |
| **v1.2** | 鏈嶅姟绔ず鏁欑偣鏂囦欢銆佷笌浠诲姟鑴氭湰浜掗攣锛堣繍琛屼腑绂佹鐐瑰姩锛?|

---

*鏂囨。鐗堟湰锛?026-05-16 路 瀵归綈 MDKOSS `PlatformDevice` 涓庣洃鎺?HTTP 鐜版湁鑳藉姏*
