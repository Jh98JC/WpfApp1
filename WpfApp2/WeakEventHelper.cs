using System;
using System.Collections.Generic;
using System.Reflection;

namespace WpfApp2
{
    // static 이벤트(ThemeManager.ThemeChanged 등)에 사용 가능한 약한 이벤트 헬퍼.
    // 구독자(메서드의 target)를 WeakReference로 보관하므로 구독 해제 호출을 빠뜨려도
    // target이 GC되면 wrapper가 다음 이벤트 발생 시 자동으로 자기 자신을 이벤트에서 분리한다.
    public static class WeakEventHelper
    {
        public static EventHandler SubscribeWeak(
            Action<EventHandler> add,
            Action<EventHandler> remove,
            EventHandler handler)
        {
            var weakTarget = handler.Target == null ? null : new WeakReference(handler.Target);
            var method = handler.Method;

            EventHandler? wrapper = null;
            wrapper = (s, e) =>
            {
                if (weakTarget == null)
                {
                    method.Invoke(null, new object?[] { s, e });
                    return;
                }
                var target = weakTarget.Target;
                if (target == null)
                {
                    // target이 GC되었으므로 자기 자신을 이벤트에서 제거
                    if (wrapper != null) remove(wrapper);
                    return;
                }
                method.Invoke(target, new object?[] { s, e });
            };
            add(wrapper);
            return wrapper;
        }

        public static Action<T> SubscribeWeak<T>(
            Action<Action<T>> add,
            Action<Action<T>> remove,
            Action<T> handler)
        {
            var weakTarget = handler.Target == null ? null : new WeakReference(handler.Target);
            var method = handler.Method;

            Action<T>? wrapper = null;
            wrapper = (arg) =>
            {
                if (weakTarget == null)
                {
                    method.Invoke(null, new object?[] { arg });
                    return;
                }
                var target = weakTarget.Target;
                if (target == null)
                {
                    if (wrapper != null) remove(wrapper);
                    return;
                }
                method.Invoke(target, new object?[] { arg });
            };
            add(wrapper);
            return wrapper;
        }
    }
}
