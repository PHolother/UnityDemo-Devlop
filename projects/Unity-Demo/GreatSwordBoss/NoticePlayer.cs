using UnityEngine;

public class NoticePlayer : MonoBehaviour
{
    private Animator animator;
    private Transform player;

    private bool inBattle;
    private bool isTurning;
    
    private float turnStartTime;
    private float turnSpeed = 9f;
    private float turnTimeDuration = 0.5f;

    private int noticePlayerHash;
    void Start()
    {
        animator = GetComponent<Animator>();
        player = GameObject.FindGameObjectWithTag("Player").transform;
        
        noticePlayerHash = Animator.StringToHash("noticePlayer");
    }

    // Update is called once per frame
    void Update()
    {
        CheckDistanceBetweenPlayer();
        UpdateTimer();
    }

    private void CheckDistanceBetweenPlayer()
    {
        if (inBattle) return;
        
        var distance = Vector3.Distance(transform.position, player.position);
        if (distance > 30) return;
        
        inBattle = true;
        animator.SetTrigger(noticePlayerHash);
    }
    
    private void TurnToPlayer()
    {
        var direction = (player.position - transform.position).normalized;
        direction.y = 0;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);
    }

    public void StimulateTimer()
    {
        isTurning = true;
        turnStartTime = Time.time;
    }
    
    private void UpdateTimer()
    {
        if (!isTurning) return;
        if (Time.time - turnStartTime > turnTimeDuration) isTurning = false;
        TurnToPlayer();
    }
}
