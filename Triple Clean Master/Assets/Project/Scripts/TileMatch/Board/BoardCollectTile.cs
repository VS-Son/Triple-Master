using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Project.Scripts.Constants;
using Project.Scripts.Effect;
using Project.Scripts.Manager;
using Project.Scripts.TileMatch.Manager;
using Project.Scripts.TileMatch.Tiles;
using Project.Scripts.UI;
using Project.Scripts.UI.Components.booster;
using Project.Scripts.UI.Screen;
using UnityEngine;

namespace Project.Scripts.TileMatch.Board
{
    public class BoardCollectTile : Singleton<BoardCollectTile>
    {
        [SerializeField] private List<Transform> slots;
        [SerializeField] private List<ParticleSystem> clearEffect;
        private readonly List<Tile> _collectedTile = new List<Tile>();
        private readonly List<Tile> _originalTile = new List<Tile>();
        private int _slotIndex;
        private int _countSlot;
        public static bool IsMatching;
        private Vector2 _originalScale;
        private readonly Queue<List<Tile>> _clearQueue = new();
        private bool _isClearing;
        public void CollectTile(Tile tile)
        {
            tile.spriteTile.sortingOrder += 10;
            tile.originalLayer = tile.currentLayer;
            _originalScale = tile.originalScale;
            _collectedTile.Add(tile);
            _originalTile.Add(tile);
            _slotIndex = _collectedTile.Count;
            _slotIndex = _countSlot;
            _countSlot++;

            if (_collectedTile.Count > 0 && !PlayScreen.IsClick && !IsMatching && GameplayManager.IsUndoUnlocked)
            {
                PlayScreen.SetBoosterAlpha(TypeBooster.Undo, 1f);
            }

            tile.transform.parent = GetSlotParent(_slotIndex);
            SetMatchThree(tile);
            if (!_isBatchCollecting)
            {
                TryShowRevive();
            }
        }

        private bool _isBatchCollecting;

        public void CollectTiles(List<Tile> tiles)
        {
            _isBatchCollecting = true;
            try
            {
                foreach (var tile in tiles)
                {
                    CollectTile(tile);
                }
            }
            finally
            {
                _isBatchCollecting = false;
            }
            TryShowRevive();
        }

        private bool _isReviveShown;

        private const int MaxBoardSlots = 7;

        public bool IsFull => _collectedTile.Count >= MaxBoardSlots;

        private bool HasPendingClear()
        {
            if (_isClearing || _clearQueue.Count > 0) return true;
            foreach (var t in _collectedTile)
            {
                if (t.isClearing) return true;
            }
            return false;
        }

        private void TryShowRevive()
        {
            if (_isReviveShown || !CheckShowRevive()) return;
            _isReviveShown = true;
            TileManager.Instance.DisableInput(false);
            DOVirtual.DelayedCall(0.5f, () =>
            {
                if (CheckShowRevive())
                {
                    UIManager.OpenPopup<PopupRevive>(PopupType.Revive);
                }
                else
                {
                    _isReviveShown = false;
                    TileManager.Instance.DisableInput(true);
                }
            });
        }

        private bool CheckShowRevive()
        {
            return !_isBatchCollecting && IsFull && !HasPendingClear();
        }

        private Transform GetSlotParent(int index)
        {
            return slots[Mathf.Clamp(index, 0, slots.Count - 1)];
        }

        private Vector3 GetSlotPosition(int index)
        {
            int last = slots.Count - 1;
            if (index <= last) return slots[Mathf.Max(0, index)].position;
            Vector3 step = slots.Count > 1 ? slots[last].position - slots[last - 1].position : Vector3.zero;
            return slots[last].position + step * (index - last);
        }

        private void SetMatchThree(Tile tile)
        {
            var matchThree = new List<Tile>();
            foreach (var t in _collectedTile)
            {
                if (t.isClearing) continue;
                if (tile.tileId == t.tileId)
                {
                    matchThree.Add(t);
                }
            }

            switch (matchThree.Count)
            {
                case 1:
                    tile.transform.DOMove(GetSlotPosition(_slotIndex), 0.3f).SetId("collect")
                        .OnComplete((ReArrangeBoard));
                    tile.transform.DOScale(1, 0.3f);
                    break;
                case 2:
                    InsertMatching(matchThree, tile, 0);
                    break;
                case 3:
                    IsMatching = true;
                    if (GameplayManager.IsUndoUnlocked)
                    {
                        PlayScreen.SetBoosterAlpha(TypeBooster.Undo, 0.6f);
                    }
                    if (PlayScreen.IsClick && GameplayManager.IsMagicWandUnlocked)
                    {
                        PlayScreen.SetBoosterAlpha(TypeBooster.MagicWand, 0.6f);
                    }

                    foreach (var t in matchThree)
                    {
                        t.isClearing = true;
                    }

                    InsertMatching(matchThree, tile, 1);
                    break;
            }
        }

        private void InsertMatching(List<Tile> matchThree, Tile tile, int index)
        {
            Tile stMatch = matchThree[index];
            int targetIndex = _collectedTile.IndexOf(stMatch) + 1;
            _slotIndex = targetIndex;
            _collectedTile.Remove(tile);
            _collectedTile.Insert(targetIndex, tile);
            DOVirtual.DelayedCall(0.1f, ReArrangeBoard);
            tile.transform.DOMove(GetSlotPosition(_slotIndex), 0.3f).OnComplete((() =>
            {
                if (index == 1 && tile != null)
                {
                    _clearQueue.Enqueue(new List<Tile>(matchThree));

                    if (!_isClearing)
                    {
                        StartCoroutine(ProcessClearQueue());
                    }
                }

            }));
            tile.transform.DOScale(1, 0.3f);


        }

        private IEnumerator ProcessClearQueue()
        {
            _isClearing = true;

            while (_clearQueue.Count > 0)
            {
                var match = _clearQueue.Dequeue();
                yield return StartCoroutine(ClearMatchedTiles(match));
                yield return new WaitForSeconds(0.1f);
            }

            _isClearing = false;
            IsMatching = false;

            TryShowRevive();
        }

        IEnumerator ClearMatchedTiles(List<Tile> tileMatch)
        {
            if (TileManager.Instance.IsLastThreeTile())
            {
                yield return StartCoroutine(LastThreeAnimation(tileMatch));
                AudioManager.Instance.PlaySfx(AudioConstants.Clear);
                _collectedTile.Clear();
                _originalTile.Clear();
                foreach (var tile in tileMatch)
                {
                    GameplayManager.PoolTile.Release(tile);
                    _countSlot--;
                }

            }
            else
            {
                yield return StartCoroutine(SequentialClearAnimation(tileMatch));
                foreach (var tile in tileMatch)
                {
                    _collectedTile.Remove(tile);
                    _originalTile.Remove(tile);
                    TileManager.Instance.DecreaseTileIndex();
                    GameplayManager.PoolTile.Release(tile);
                    _countSlot--;
                }
            }


            ReArrangeBoard();

            IsMatching = EffectManager.IsLive;
            PlayScreen.IsClick = false;
            if (!PlayScreen.IsClick && GameplayManager.IsMagicWandUnlocked)
            {
                DOVirtual.DelayedCall(0.3f, () => PlayScreen.SetBoosterAlpha(TypeBooster.MagicWand, 1f));
            }

            if (HasTileInBoard() && _clearQueue.Count <= 0)
            {
                DOVirtual.DelayedCall(0.3f,(() => PlayScreen.SetBoosterAlpha(TypeBooster.Undo, 1f) )) ;
            }
        }

        private const float ClearPopTime = 0.08f;
        private const float ClearShrinkTime = 0.14f;
        private const float ClearStagger = 0.07f;
        private const float ClearPopScale = 1.12f;
        private readonly HashSet<Tile> _mergingTiles = new();

        private IEnumerator SequentialClearAnimation(List<Tile> tiles)
        {
            Sequence seq = DOTween.Sequence();
            for (int i = 0; i < tiles.Count; i++)
            {
                Tile tile = tiles[i];
                _mergingTiles.Add(tile);
                tile.transform.DOKill();

                float start = i * ClearStagger;
                Vector3 baseScale = Vector3.one;
                int listIndex = _collectedTile.IndexOf(tile);
                Vector3 slotPos = listIndex >= 0 ? GetSlotPosition(listIndex) : tile.transform.position;
                int effectIndex = slots.IndexOf(tile.transform.parent);

                seq.Insert(start, tile.transform.DOMove(slotPos, ClearPopTime).SetEase(Ease.OutQuad));
                seq.Insert(start, tile.transform.DOScale(baseScale * ClearPopScale, ClearPopTime).SetEase(Ease.OutQuad));
                seq.Insert(start + ClearPopTime,
                    tile.transform.DOScale(0f, ClearShrinkTime).SetEase(Ease.InBack, 1.5f));

                bool isFirst = i == 0;
                seq.InsertCallback(start + ClearPopTime + ClearShrinkTime * 0.7f, () =>
                {
                    if (effectIndex >= 0)
                    {
                        StartCoroutine(EffectManager.PlayEffectAndWait(clearEffect, effectIndex));
                    }
                    if (isFirst)
                    {
                        AudioManager.Instance.PlaySfx(AudioConstants.Clear);
                    }
                });
            }

            yield return seq.WaitForCompletion();
            foreach (var tile in tiles)
            {
                _mergingTiles.Remove(tile);
            }
        }

        private IEnumerator LastThreeAnimation(List<Tile> tiles)
        {
            yield return new WaitForSeconds(0.15f);
            Tile first = tiles[0];
            Tile second = tiles[1];
            Tile third = tiles[2];
            Vector3 slotTarget = slots[0].position;
            Vector3 firstMovePos = slotTarget + Vector3.down * 1f;
            int effectIndex = slots.IndexOf(first.transform.parent);

            first.transform.DORotate(new Vector3(0, 0, -360), 1.3f, RotateMode.FastBeyond360).SetEase(Ease.InOutSine);
            yield return DOTween.Sequence().Join(first.transform.DOMove(firstMovePos, 0.2f).SetEase(Ease.OutQuad))
                .WaitForCompletion();

            Sequence dilate = DOTween.Sequence();
            dilate.Join(second.transform.DOMove(second.transform.position + Vector3.right * 0.5f, 0.2f));
            dilate.Join(third.transform.DOMove(third.transform.position + Vector3.right * 0.5f, 0.2f));
            yield return dilate.WaitForCompletion();

            Sequence merge = DOTween.Sequence();
            merge.Join(first.transform.DOMove(slotTarget, 0.4f).SetEase(Ease.InBack));
            merge.Join(second.transform.DOMove(slotTarget, 0.3f).SetEase(Ease.InBack));
            merge.Join(third.transform.DOMove(slotTarget, 0.3f).SetEase(Ease.InBack));

            yield return merge.WaitForCompletion();
            yield return DOTween.Sequence().Join(first.transform.DOScale(0, 0.12f))
                .Join(second.transform.DOScale(0, 0.12f)).Join(third.transform.DOScale(0, 0.12f)).WaitForCompletion();
            yield return new WaitForSeconds(0.1f);
            yield return StartCoroutine(EffectManager.PlayEffectAndWait(clearEffect, effectIndex,(() =>
            {
                DOVirtual.DelayedCall(0.1f, (() =>
                {
                    StateUI.ChangeState(ScreenType.NextScreen);
                }));
            }))) ;


        }

        private void ReArrangeBoard()
        {
            DOTween.Kill("collect");
            for (int i = 0; i < _collectedTile.Count; i++)
            {
                var tile = _collectedTile[i];
                tile.transform.parent = GetSlotParent(i);
                if (_mergingTiles.Contains(tile)) continue;
                tile.transform.DOMove(GetSlotPosition(i), 0.25f).SetEase(Ease.OutCubic);
                tile.transform.DOScale(1, 0.15f).SetEase(Ease.OutQuad);
            }
        }

        public void UndoTile(int quantity)
        {
            int count = Math.Min(quantity, _originalTile.Count);
            bool isRevive = quantity > 1;

            Sequence undoSeq = DOTween.Sequence();
            for (int i = 0; i < count; i++)
            {
                int index = _originalTile.Count - 1 - i;
                Tile tile = _originalTile[index];
                tile.transform.DOKill();
                tile.transform.localRotation = Quaternion.identity;
                _collectedTile.Remove(tile);
                tile.SetDataUndo();
                undoSeq.Join(tile.transform.DOLocalMove(tile.originalPos, 0.4f).SetEase(Ease.Flash));
                undoSeq.Join(tile.transform.DOScale(_originalScale, 0.4f));
            }
            TileManager.Instance.InitCoverageCount();

            if (isRevive)
            {
                TileManager.Instance.DisableInput(false);
                undoSeq.OnComplete(() =>
                    DOVirtual.DelayedCall(0.1f, () =>
                        TileManager.Instance.ShuffleGridTiles(() => TileManager.Instance.DisableInput(true))));
            }

            _originalTile.RemoveRange(_originalTile.Count - count, count);
            _isReviveShown = false;
            _countSlot -= count;
            ReArrangeBoard();
            if (_collectedTile.Count <= 0)
            {
                PlayScreen.SetBoosterAlpha(TypeBooster.Undo, 0.6f);
            }

        }

        public void ResetBoard()
        {
            foreach (var tile in _collectedTile)
            {
                Destroy(tile.gameObject);
            }

            _slotIndex = 0;
            _countSlot = 0;
            _collectedTile.Clear();
            _originalTile.Clear();
            _clearQueue.Clear();
            _mergingTiles.Clear();
            _isReviveShown = false;
            _isClearing = false;
            IsMatching = false;
            PlayScreen.IsClick = false;
        }

        public List<Tile> GetListCollect()
        {
            return new List<Tile>(_collectedTile);
        }

        public static bool HasTileInBoard()
        {
            return Instance._collectedTile.Count > 0;
        }
    }
}
