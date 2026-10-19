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


    void ChangeControlledPieceColor()
    {
        if (controlledPiece == null)
        {
            return;
        }

        PieceColor pieceColor =
            controlledPiece.GetComponent<PieceColor>();

        pieceColor.ToggleColor(
            blueMaterial,
            orangeMaterial);
        CutAnalytics.RecordMechanic(Mechanic.ColorShift, "key_shift");
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
