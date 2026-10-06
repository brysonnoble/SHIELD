using PA_DronePack;
using UnityEngine;

// Flies a spawned drone from wherever it is to a target position at a set
// cruise speed, then hovers there. DroneSpawner adds this to a drone when
// its SPAWN command carries an end position and speed.
//
// Rather than moving the transform directly, this drives the drone pack's
// own DroneController through the same DriveInput/StrafeInput/LiftInput/
// TurnInput calls the keyboard-driven DroneAxisInput makes, so the drone
// keeps the pack's physics: it pitches and banks into its direction of
// travel, eases in and out of speed, and yaws to fly nose-first.
// DroneAxisInput is disabled on the drone so the keyboard can't fight the
// autopilot.
[RequireComponent(typeof(DroneController))]
public class DroneAutopilot : MonoBehaviour
{
    // Cruise speed along the straight line to the target, in m/s.
    public float speed = 5f;
    public Vector3 targetPosition;

    // How quickly the commanded speed may change, in m/s^2 - both when
    // pulling away from the start and when braking to arrive at the target.
    // DroneController on its own reaches any commanded speed within a few
    // physics steps, which looks unrealistically abrupt at higher speeds.
    public float maxAcceleration = 4f;

    // Yaw rate cap (rad/s) and proportional gain for turning nose-first
    // toward the target.
    public float maxYawRate = 1.5f;
    public float yawGain = 2f;

    // Within this distance (m) of the target the drone is considered to
    // have arrived; it keeps station-holding on the target afterwards.
    public float arrivalRadius = 0.25f;

    private DroneController controller;
    private float commandedSpeed;
    private bool arrived;

    private void Start()
    {
        controller = GetComponent<DroneController>();

        DroneAxisInput axisInput = GetComponent<DroneAxisInput>();
        if (axisInput != null)
            axisInput.enabled = false;

        // DroneController maps each input in [-1, 1] onto these speeds, so
        // setting them all to the cruise speed lets FixedUpdate() pass
        // velocities through as (velocity / speed).
        controller.AdjustSpeed(speed);
        controller.AdjustStrafe(speed);
        controller.AdjustLift(speed);
    }

    private void FixedUpdate()
    {
        if (!controller.enabled || !controller.motorOn)
            return; // crashed (DroneController cuts the motor on a hard collision) - let it fall

        Vector3 toTarget = targetPosition - transform.position;
        float distance = toTarget.magnitude;

        if (!arrived && distance <= arrivalRadius)
        {
            arrived = true;
            Debug.Log($"[DroneAutopilot] {name} reached {targetPosition}.");
        }

        // Fastest speed from which the drone can still brake to a stop at
        // the target under maxAcceleration, capped at the cruise speed.
        float desiredSpeed = Mathf.Min(speed, Mathf.Sqrt(2f * maxAcceleration * distance));
        commandedSpeed = Mathf.MoveTowards(commandedSpeed, desiredSpeed, maxAcceleration * Time.fixedDeltaTime);

        Vector3 desiredVelocity = distance > 0.01f ? toTarget / distance * commandedSpeed : Vector3.zero;

        // DroneController applies drive/strafe in the drone's own frame;
        // use its yaw-only frame so pitch/bank don't skew the split.
        Quaternion yawFrame = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Vector3 localVelocity = Quaternion.Inverse(yawFrame) * new Vector3(desiredVelocity.x, 0f, desiredVelocity.z);

        controller.DriveInput(Mathf.Clamp(localVelocity.z / speed, -1f, 1f));
        controller.StrafeInput(Mathf.Clamp(localVelocity.x / speed, -1f, 1f));
        controller.LiftInput(Mathf.Clamp(desiredVelocity.y / speed, -1f, 1f));

        // Turn nose-first along the horizontal path. Near the target (or on
        // a purely vertical path) there's no meaningful heading to chase,
        // so hold the current one.
        Vector3 flatToTarget = new Vector3(toTarget.x, 0f, toTarget.z);
        float yawRate = 0f;
        if (flatToTarget.magnitude > 1f)
        {
            Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z);
            float headingError = Vector3.SignedAngle(flatForward, flatToTarget, Vector3.up) * Mathf.Deg2Rad;
            yawRate = Mathf.Clamp(headingError * yawGain, -maxYawRate, maxYawRate);
        }
        controller.TurnInput(controller.turnSensitivty > 0f ? yawRate / controller.turnSensitivty : 0f);
    }
}
