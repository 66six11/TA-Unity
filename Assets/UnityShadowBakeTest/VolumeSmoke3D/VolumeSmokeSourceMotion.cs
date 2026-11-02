using UnityEngine;

namespace TA.VolumeSmoke3D
{
    public sealed class VolumeSmokeSourceMotion : MonoBehaviour
    {
        public bool animate;
        public float distance=0.65f;
        public float speed=0.7f;
        Vector3 initialPosition;
        Quaternion initialRotation;
        float time;
        void OnEnable(){initialPosition=transform.position;initialRotation=transform.rotation;time=0;}
        void Update()
        {
            if(!animate)return;
            time+=Time.deltaTime*speed;
            transform.position=initialPosition+new Vector3(Mathf.Sin(time)*distance,Mathf.Sin(time*0.7f)*0.15f,Mathf.Cos(time*0.8f)*0.25f);
            transform.rotation=Quaternion.Euler(0,Mathf.Sin(time)*35,Mathf.Sin(time*0.6f)*12)*initialRotation;
        }
    }
}
