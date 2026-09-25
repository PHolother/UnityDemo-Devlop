using UnityEngine;

public class MoveCube : MonoBehaviour
{
    public GetPlayerInput playerInput;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (playerInput.isForward)
        {
            transform.Translate(Vector3.forward *  Time.deltaTime);
        }

        if (playerInput.isBack)
        {
            transform.Translate(Vector3.back *  Time.deltaTime);
        }

        if (playerInput.isLeft)
        {
            transform.Translate(Vector3.left *  Time.deltaTime);
        }

        if (playerInput.isRight)
        {
            transform.Translate(Vector3.right *  Time.deltaTime);
        }

        if (playerInput.isUp)
        {
            transform.Translate(Vector3.up *  Time.deltaTime);
        }

        if (playerInput.isDown)
        {
            transform.Translate(Vector3.down *  Time.deltaTime);
        }
    }
}
