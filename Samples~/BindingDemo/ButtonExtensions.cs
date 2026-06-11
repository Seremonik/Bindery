using R3;
using UnityEngine.UIElements;

namespace Bindery
{

public static class ButtonExtensions
{
    public static Observable<Unit> OnClickAsObservable(this Button button)
    {
        return Observable.FromEvent(
            h => button.clicked += h,
            h => button.clicked -= h);
    }
}

} // namespace Bindery
