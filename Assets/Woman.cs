/***************************************************************************
// File       : Woman.cs
// Author     : Panyuxuan
// Created    : 2025/08/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Add script summary here
// ***************************************************************************/

using System;
using UnityEngine;

public class Woman : MonoBehaviour
{
    Animator animator;
    public float speed = 1f;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        animator.Play("Walk");
    }

    private void Update()
    {
        transform.Translate(Vector3.forward * speed * Time.deltaTime);
    }
}
