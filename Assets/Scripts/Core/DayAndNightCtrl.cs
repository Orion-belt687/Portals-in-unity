using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DayAndNightCtrl : MonoBehaviour
{
    [Header("需要指定场景中的方向光")]
    public Light sunLight;

    // 10分钟 = 600秒
    private const float dayDuration = 60f;

    void Update()
    {
        if (sunLight != null)
        {
            // 每秒旋转的角度
            float degreesPerSecond = 360f / dayDuration;
            sunLight.transform.Rotate(Vector3.right, degreesPerSecond * Time.deltaTime, Space.World);
        }
    }
}
