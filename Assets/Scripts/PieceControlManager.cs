using System.Collections.Generic;
using UnityEngine;
using Team3.Analytics;

public class PieceControlManager : MonoBehaviour
{
    public Material blueMaterial;
    public Material orangeMaterial;

    public Material blueSelectMaterial;
    public Material orangeSelectMaterial;


    public CutController cutController;
    public CameraRig cameraRig;
    public LevelManager levelManager;
    public HUD hud;

    public KeyCode colorKey = KeyCode.LeftShift;

    private readonly List<CuttablePiece> pieces =
        new List<CuttablePiece>();

    private CuttablePiece controlledPiece;

    void Start()
    {
        RegisterPiece(
            cutController.player,
            PieceColor.PieceColorType.Blue);

        SetControlledPiece(cutController.player);
    }

    void Update()
    {
        if (cutController.state != CutController.State.Normal)
        {
            return;
        }

        if (Input.GetKeyDown(colorKey))
        {
            ChangeControlledPieceColor();
        }
    }

    public void RegisterPiece(
        CuttablePiece piece,
        PieceColor.PieceColorType startingColor)
    {
        if (piece == null || pieces.Contains(piece))
        {
            return;
        }

        pieces.Add(piece);

        PieceColor pieceColor = piece.GetComponent<PieceColor>();
        if (pieceColor == null)
        {
            pieceColor = piece.gameObject.AddComponent<PieceColor>();
        }

        pieceColor.SetColor(
            startingColor,
            blueMaterial,
            orangeMaterial);
    }

    public PieceColor.PieceColorType GetControlledColor()
    {
        PieceColor pieceColor =
            controlledPiece.GetComponent<PieceColor>();

        return pieceColor.currentColor;
    }

    public Material GetSelectMaterial(PieceColor.PieceColorType color)
    {
        if (color == PieceColor.PieceColorType.Blue)
        {
            return blueSelectMaterial;
        }
        return orangeSelectMaterial;
    }


    public event System.Action ColorInput;
    public event System.Action<CuttablePiece, ColorObstacle> ColorRefused;
    public event System.Action ColorChanged;

    public void ChangeControlledPieceColor(string reason = "key_shift")
    {
        ColorInput?.Invoke();
        if (controlledPiece == null || cutController.state != CutController.State.Normal)
        {
            return;
        }

        PieceColor pieceColor =
            controlledPiece.GetComponent<PieceColor>();

        ColorObstacle holding = FindHoldingGlass(controlledPiece, pieceColor.currentColor);
        if (holding != null)
        {
            ColorRefused?.Invoke(controlledPiece, holding);
            return;
        }

        pieceColor.ToggleColor(
            blueMaterial,
            orangeMaterial);
        CutAnalytics.RecordMechanic(Mechanic.ColorShift, reason);
        ColorChanged?.Invoke();
    }

    static ColorObstacle FindHoldingGlass(CuttablePiece piece, PieceColor.PieceColorType color)
    {
        Collider body = piece.GetComponent<Collider>();
        if (body == null || !body.enabled)
        {
            return null;
        }

        foreach (ColorObstacle glass in FindObjectsByType<ColorObstacle>(FindObjectsSortMode.None))
        {
            if (glass.obstacleColor != color)
            {
                continue;
            }
            Collider glassCollider = glass.GetComponent<Collider>();
            if (glassCollider == null || !glassCollider.enabled || !glassCollider.bounds.Intersects(body.bounds))
            {
                continue;
            }
            Vector3 direction;
            float distance;
            if (Physics.ComputePenetration(
                body, body.transform.position, body.transform.rotation,
                glassCollider, glassCollider.transform.position, glassCollider.transform.rotation,
                out direction, out distance) && distance > 0.01f)
            {
                return glass;
            }
        }
        return null;
    }

    void SetControlledPiece(CuttablePiece newPiece)
    {
        if (controlledPiece != null)
        {
            PlayerController oldController =
                controlledPiece.GetComponent<PlayerController>();

            if (oldController != null)
            {
                oldController.StopPlayer();
                oldController.canMove = false;
            }

            SetInactiveConstraints(controlledPiece);
        }

        controlledPiece = newPiece;

        PlayerController newController =
            controlledPiece.GetComponent<PlayerController>();

        if (newController == null)
        {
            newController =
                controlledPiece.gameObject.AddComponent<PlayerController>();

            newController.camScript = cameraRig;
            newController.speed = cutController.playerScript.speed;
            newController.jumpForce =
                cutController.playerScript.jumpForce;
        }

        newController.camScript = cameraRig;
        newController.canMove = true;

        Rigidbody rb = controlledPiece.GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        cutController.SetControlledPiece(
            controlledPiece,
            newController);

        cameraRig.player = controlledPiece.transform;
        levelManager.player = controlledPiece;

        if (hud != null)
        {
            hud.player = controlledPiece;
        }
    }

    void SetInactiveConstraints(CuttablePiece piece)
    {
        Rigidbody rb = piece.GetComponent<Rigidbody>();

        RigidbodyConstraints constraints =
            RigidbodyConstraints.FreezeRotation;

        if (!cutController.canPushLeftover)
        {
            constraints |= RigidbodyConstraints.FreezePositionX;
            constraints |= RigidbodyConstraints.FreezePositionZ;
        }

        rb.constraints = constraints;
    }
}
