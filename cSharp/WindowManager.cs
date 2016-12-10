using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;


/// <summary>
/// 窗口管理器
/// </summary>
[SLua.CustomLuaClass]
public static class WindowManager {
    /// <summary>
    /// 创建窗口的回调
    /// </summary>
    /// <param name="windowName">窗口名</param>
    public delegate void WindowCreateCallback(string windowName, GameObject windowObj, bool isPopup);
    public static WindowCreateCallback CreateCallback;

    /// <summary>
    /// 销毁窗口的回调
    /// </summary>
    /// <param name="windowName">窗口名</param>
    public delegate void WindowDestroyCallback(string windowName);
    public static WindowDestroyCallback DestroyCallback;

    /// <summary>窗口的父节点</summary>
    public static Transform Container { get; private set; }

    /// <summary>当前窗口</summary>
    public static GameObject Current { get; private set; }

    /// <summary>顶层窗口</summary>
    public static GameObject Top
    {
        get
        {
            if (popup_stack_.Count > 0)
                return popup_stack_.Peek();
            else
                return Current;
        }
    }

    /// <summary>水平层级窗口故事链，对应普通窗口</summary>
    private static Stack<string> storyboard_ = new Stack<string>();
    /// <summary>垂直层级窗口堆栈，对应弹出窗口</summary>
    private static Stack<GameObject> popup_stack_ = new Stack<GameObject>();
    private static List<GameObject> popup_list_ = new List<GameObject>();
    /// <summary>弹出窗口遮罩</summary>
    private static GameObject block;
    /// <summary>Mediator强制初始化lua函数</summary>
    private static SLua.LuaFunction mediatorInit;
    /// <summary>参数传递用的栈帧</summary>
    /// <remarks>
    /// 窗口的创建和销毁类似函数调用，创建的时候可以带参数，销毁时有返回值
    /// 管理器内部维护了一个模拟的函数调用栈帧
    /// 
    /// Frame标记帧位置，Top标记栈顶位置，每次调用和回退都会修改T和F的值
    /// [初始]F=-1 T=-1
    /// [调用]
    /// T = -1：不保存F
    /// T != -1：F需要入栈，Stack[T+1] = F, F = T+1
    /// [回退]
    /// F = -1：T = -1回到初始状态
    /// F != -1：T = F - 1, F = Stack[F]
    /// 
    /// 初始状态   窗口1传递12 34  窗口2传递56   窗口3传递78  销毁窗口3    销毁窗口2    销毁窗口1
    /// |------|     |------|     |------|    |------|    |------|    |------|    |------|
    /// |      |     |      |     |      |    |  78  |T   |      |    |      |    |      |
    /// |      |     |      |     |      |    |--(2)-|F   |      |    |      |    |      |
    /// |      |     |      |     |  56  |T   |  56  |    |  56  |T   |      |    |      |
    /// |      |     |      |     |-(-1)-|F   |-(-1)-|    |-(-1)-|F   |      |    |      |
    /// |      |     |  34  |T    |  34  |    |  34  |    |  34  |    |  34  |T   |      |
    /// |      |     |  12  |     |  12  |    |  12  |    |  12  |    |  12  |    |      |
    /// |------|FT   |------|F    |------|    |------|    |------|    |------|F   |------|FT
    /// </remarks>
    private static object[] stackFrame = new object[1024];
    /// <summary>帧位置</summary>
    private static int frame = -1;
    /// <summary>栈定位置</summary>
    private static int top = -1;
    /// <summary>返回值数量</summary>
    private static int numReturnValues;

    private static GameObject blockCanvas;
    private static GameObject cameraObj;

    /// <summary>
    /// 静态构造函数
    /// </summary>
    static WindowManager()
    {
        cameraObj = GameObject.FindGameObjectWithTag("UICamera");
        blockCanvas = GameObject.Find("BlockCanvas");
    }

    /// <summary>
    /// 设置UI相机的audio listener是否起效
    /// </summary>
    /// <param name="value"></param>
    public static void SetUIAudioListenerEnable(bool value)
    {
        //GameObject cameraObj = GameObject.FindGameObjectWithTag("UICamera");
		cameraObj.GetComponent<AudioListener>().enabled = value;
    }

    /// <summary>
    /// 创建窗口时，参数入栈
    /// </summary>
    /// <param name="values"></param>
    private static void PushFrame(params object[] values)
    {
        // 栈非空，存储返回地址
        if (top != -1)
        {
            stackFrame[++top] = frame;
            frame = top;
        }
        for (int i = 0; i < values.Length; ++i)
        {
            stackFrame[++top] = values[i];
        }
    }

    /// <summary>
    /// 销毁窗口时，调用堆栈回退
    /// </summary>
    private static void PopFrame()
    {
        // 栈空，直接返回原始状态
        if (frame == -1)
        {
            top = frame;
        }
        else
        {
            top = frame - 1;
            // 获取返回地址
            frame = (int)stackFrame[frame];
        }
    }

    /// <summary>
    /// 获得参数数量
    /// </summary>
    /// <returns></returns>
    public static int GetParamCount()
    {
        return top - frame;
    }

    /// <summary>
    /// 获得参数
    /// </summary>
    /// <param name="index">参数索引</param>
    /// <returns></returns>
    public static object GetParam(int index)
    {
        return stackFrame[frame + index + 1];
    }

    /// <summary>
    /// 弹出所有返回值
    /// </summary>
    public static void PopReturnValues()
    {
        while (--numReturnValues >= 0) --top;
    }

    /// <summary>
    /// 切换窗口，对应水平层级
    /// </summary>
    /// <param name="windowName"></param>
    /// <param name="values">可变参数列表</param>
    /// <returns></returns>
	public static GameObject SwitchWindow(string windowName, params object[] values)
	{
        // 同样的窗口，退出
        if (storyboard_.Count > 0 && storyboard_.Peek() == windowName)
            return null;

		GameObject wnd = CreateWindow(windowName, false);
		if (wnd == null)
			return null;

        // 如果故事板中已经存在改窗口，直接跳到该位置，其余销毁
        if (storyboard_.Contains(windowName))
        {
            while (storyboard_.Peek() != windowName)
            {
                storyboard_.Pop();
                PopFrame();
            }
            PopFrame();
        }
        else
        {
            storyboard_.Push(windowName);
        }

        if (Current != null)
        {
            DestroyWindow(Current);
            Current = null;
        }

        // 参数入栈
		PushFrame(values);

        // 切换窗口回调
		Current = wnd;
		return Current;
	}

    /// <summary>
    /// 水平层级回退
    /// </summary>
    /// <param name="values">可变参数列表</param>
    /// <returns></returns>
    public static GameObject RollBack(params object[] values)
    {
        AudioManager.instance.Play(cameraObj.transform, "SE_UI_Click_Back");
        if (storyboard_.Count == 1 || Current == null)
            return null;

        if (Current != null)
        {
            DestroyWindow(Current);
            Current = null;
        }

        // 参数调用栈回退
        PopFrame();

        // 返回值入栈
        for (int i = 0; i < values.Length; ++i)
        {
            stackFrame[++top] = values[i];
        }
        numReturnValues = values.Length;

        storyboard_.Pop();

        Current = CreateWindow(storyboard_.Peek(), false);
        return Current;
    }

    private static string stash;
    /// <summary>
    /// 暂存窗口故事板状态
    /// </summary>
    public static void SaveStash()
    {
		if (Current == null || (!string.IsNullOrEmpty(stash) && storyboard_.Contains(stash)))
            return;
        stash = storyboard_.Peek();
    }


    /// <summary>
    /// 返回窗口故事板状态
    /// </summary>
    /// <returns></returns>
    public static GameObject LoadStash()
    {
        if (storyboard_.Count == 0 || string.IsNullOrEmpty(stash))
            return null;
        return SwitchWindow(stash);
    }

    /// <summary>
    /// 压入弹出窗口，对应垂直层级
    /// </summary>
    /// <param name="windowName"></param>
    /// <param name="values">可变参数列表</param>
    /// <returns></returns>
 /*   public static GameObject PushWindow(string windowName, params object[] values)
    {
        // 遮罩不存在则创建
        if (block == null)
        {
			Object blockPrefab = AssetManager.Instance.Load("Resources/prefabs/UI/Controller/PopupBlock") as Object;
            if (blockPrefab != null)
                block = GameObject.Instantiate(blockPrefab) as GameObject;
            block.transform.SetParent(blockCanvas.transform, false);

            // 注册点击遮罩的事件处理
            block.GetComponent<Button>().onClick.AddListener(() =>
            {
                GameObject popup = popup_stack_.Peek();
                LuaBehavior lua = popup.GetComponent<LuaBehavior>();
                if (lua != null) lua.CallMethod("Close");
            });
        }
        block.transform.SetAsLastSibling();

        GameObject wnd = CreateWindow(windowName, true);
        if (wnd != null)
        {
            wnd.transform.SetParent(blockCanvas.transform, false);
            wnd.transform.SetAsLastSibling();
            popup_stack_.Push(wnd);
        }

        // 参数入栈
        PushFrame(values);
        return wnd;
    }

    /// <summary>
    /// 弹出窗口，对应垂直层级
    /// </summary>
    /// <param name="values"></param>
    public static void PopWindow(params object[] values)
    {
        if (popup_stack_.Count == 0)
            return;

        // 参数调用栈回退
        PopFrame();

        // 返回值入栈
        for (int i = 0; i < values.Length; ++i)
        {
            stackFrame[++top] = values[i];
        }
        numReturnValues = values.Length;

        GameObject wnd = popup_stack_.Pop();
        DestroyWindow(wnd);
        wnd = null;

        // 垂直层级为空，删除遮罩，不然调整顶层窗口的位置
        if (popup_stack_.Count == 0)
        {
            GameObject.Destroy(block);
            block = null;
        }
        else
        {
            popup_stack_.Peek().transform.SetAsLastSibling();
        }

        // 弹出时带参数，下面的窗口会接受到返回值，强制调用Mediator.Init
        if (numReturnValues > 0)
        {
            string wndName = null;
            // 顶层弹出式窗口接受参数
            if (popup_stack_.Count > 0)
            {
                wndName = popup_stack_.Peek().name;
            }
            else // 普通窗口接受参数
            {
                TabView tabView = Current.GetComponentInChildren<TabView>();
                if (tabView == null)
                {
                    wndName = Current.name;
                }
                else // 如果窗口带TabView，那么由TabView.Content调用Init
                {
                    wndName = tabView.Content.name;
                }
            }

            if (mediatorInit == null)
                mediatorInit = SLua.LuaState.main.getFunction("MediatorForceInit");
            mediatorInit.call(wndName.Replace("(Clone)", ""));
        }
    }

    /// <summary>
    /// 弹出所有窗口，对应垂直层级
    /// </summary>
    public static void PopAllWindow()
    {
        while (popup_stack_.Count > 0)
        {
            PopWindow();
        }
        //LuaUtil.Cleanup();
    }
    */
    /// <summary>
    /// 创建窗口
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    private static GameObject CreateWindow(string type, bool isPopup)
    {
        //Audio about open window
        try {
            AudioManager.instance.Play(cameraObj.transform, "SE_UI_Window_Open");
        }
        catch {
            // Do nothing?
        }
        
		if (Container == null)
		{
			Container = GameObject.Find("UI/UICanvas/WindowLayer").transform;
			Object.DontDestroyOnLoad(Container.parent.parent.gameObject);
		}

        Object prefab = AssetManager.Instance.Load("Resources/prefabs/UI/" + type) as Object; 
        if (prefab == null)
        {
            Debug.LogWarningFormat("Cannot find UI prefab: {0}", type.ToString());
            return null;
        }

        (prefab as GameObject).SetActive(false);
        GameObject obj = GameObject.Instantiate(prefab) as GameObject;
        obj.transform.SetParent(Container, false);
        obj.transform.SetAsLastSibling();
        if (CreateCallback != null)
            CreateCallback(type.Substring(type.LastIndexOf('/') + 1), obj, isPopup);
        obj.SetActive(true);
        (prefab as GameObject).SetActive(true);
        return obj;
    }

    private static void DestroyWindow(GameObject wnd)
    {
        GameObject.Destroy(wnd);
        string wndName = wnd.name.Replace("(Clone)", "");
        if (DestroyCallback != null)
            DestroyCallback(wndName);
    }

    public static GameObject PushWindow(string windowName, params object[] values)
    {
        // 遮罩不存在则创建
        if (block == null)
        {
            Object blockPrefab = AssetManager.Instance.Load("Resources/prefabs/UI/Controller/PopupBlock") as Object;
            if (blockPrefab != null)
                block = GameObject.Instantiate(blockPrefab) as GameObject;
            block.transform.SetParent(blockCanvas.transform, false);

            // 注册点击遮罩的事件处理
            block.GetComponent<Button>().onClick.AddListener(() =>
            {
                GameObject popup = popup_list_[popup_list_.Count - 1];
                LuaBehavior lua = popup.GetComponent<LuaBehavior>();
                if (lua != null) lua.CallMethod("Close");
            });
        }
        block.transform.SetAsLastSibling();

        GameObject wnd = CreateWindow(windowName, true);
        if (null != wnd)
        {
            wnd.transform.SetParent(blockCanvas.transform);
            wnd.transform.SetAsLastSibling();
            popup_list_.Add(wnd);
        }

        PushFrame(values);
        return wnd;
    }
    /// <summary>
    /// 弹出popup窗口不再传递参数
    /// </summary>
    public static void PopWindow()
    {
        if (popup_list_.Count == 0)
            return;

        PopFrame();

        PopWindowByIndex(popup_list_.Count - 1);
    }
    public static GameObject PushWindowEx(string windowName, Transform parent, params object[] values)
    {
        GameObject wnd = PushWindow(windowName, values);
        if (null != parent)
        {
            wnd.transform.SetParent(parent);
        }

        return wnd;
    }

    /// <summary>
    /// 根据窗口名删除指定的popup窗口
    /// </summary>
    /// <param name="winName"></param>
    /// <param name="values"></param>
    public static void PopWindowEx(string wndName)
    {
        int idx = GetPopWindowIndex(wndName);
        if (idx < 0 || idx >= popup_list_.Count)
            return;

        // adjust stack frame

        PopWindowByIndex(idx);
    }


    /// <summary>
    /// 删除指定数量的最上面的窗口
    /// </summary>
    /// <param name="num"> 需要弹出窗口的数量</param>
    public static void PopWindows(int num)
    {
        num = num > popup_list_.Count ? popup_list_.Count : num;
        while (num-- > 0)
        {
            PopWindow();
        }
    }
    public static void PopAllWindow()
    {
        while (popup_list_.Count > 0)
        {
            PopWindow();
        }
    }

    private static void PopWindowByIndex(int idx)
    {
        GameObject wnd = popup_list_[idx];
        popup_list_.Remove(wnd);
        DestroyWindow(wnd);

        // 垂直层级为空，删除遮罩，不然调整顶层窗口的位置
        if (popup_list_.Count == 0)
        {
            GameObject.Destroy(block);
            block = null;
        }
        else
        {
            popup_list_[popup_list_.Count - 1].transform.SetAsLastSibling();
        }
    }

    private static int GetPopWindowIndex(string wndName)
    {
        for (int i = popup_list_.Count - 1; i >= 0; --i)
        {
            GameObject wnd = popup_list_[i];
            if (wndName == wnd.name)
            {
                return i;
            }
        }

        return -1;
    }
}
