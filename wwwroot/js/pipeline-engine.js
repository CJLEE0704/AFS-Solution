(function(global){
  const HOLD_REASON={
    UPSTREAM_NOT_READY:'UPSTREAM_NOT_READY',
    DOWNSTREAM_BUSY:'DOWNSTREAM_BUSY',
    MACHINE_FAULT:'MACHINE_FAULT',
    NO_QUEUE:'NO_QUEUE'
  };

  function chooseBendingTarget(cands){
    const scored=(cands||[]).map(c=>({
      id:c,
      eta:(global.stageSlots?.[c]?.eta)||0,
      qlen:(global.stageSlots?.[c]?.queueLen)||0,
      fault:(global.machSt?.[c]?.alarm)?1:0,
      compatible:(global.stageSlots?.[c]?.compatible===false)?1:0
    }));
    scored.sort((a,b)=>(a.fault-b.fault)||(a.compatible-b.compatible)||(a.eta-b.eta)||(a.qlen-b.qlen));
    return scored[0]?.id||cands?.[0]||1;
  }

  function nextPipeFromActiveProject(){
    const pid=ProjectStore.state.activeProjectId || global.activeProjectId;
    if(!pid) return null;
    const q=ProjectStore.ensureQueue(pid);
    if(!q.length) return null;
    const pipeId=q[0];
    return ProjectStore.findPipe(pipeId)?.pipe || null;
  }

  function syncPipeSelection(pipe){
    if(!pipe) return;
    if(typeof global.selOpPipe==='function') global.selOpPipe(pipe);
    if(typeof global.selWorkerPipe==='function') global.selWorkerPipe(pipe.id);
  }

  function bind(){
    function getPipeByIdx(idx){
      const list=global.PIPE_DATA||[];
      if(!list.length || idx==null) return null;
      return list[idx % list.length] || null;
    }

    function setPipeStatusById(pipeId, status, prog){
      (global.PIPE_DATA||[]).forEach(p=>{
        if(p.id===pipeId){ p.status=status; if(typeof prog==='number') p.prog=prog; }
      });
      (global.projects||[]).forEach(prj=>{
        (prj.pipes||[]).forEach(p=>{
          if(p.id===pipeId){ p.status=status; if(typeof prog==='number') p.prog=prog; }
        });
      });
    }

    function normalizeInitialPipeStates(){
      const list=global.PIPE_DATA||[];
      list.forEach(p=>{
        p.status='대기';
        p.prog=0;
        p.delay=0;
        p.needsReview=false;
      });
      (global.projects||[]).forEach(prj=>{
        (prj.pipes||[]).forEach(p=>{
          p.status='대기';
          p.prog=0;
          p.delay=0;
          p.needsReview=false;
        });
      });
    }

    function syncStagePipeBadges(){
      const slots=global.stageSlots||{};
      const s4 = getPipeByIdx(slots[4]?.pipeIdx);
      const s3 = getPipeByIdx(slots[3]?.pipeIdx);
      const s2 = getPipeByIdx(slots[2]?.pipeIdx);
      const s5 = getPipeByIdx(slots[5]?.pipeIdx);
      const s1 = getPipeByIdx(slots[1]?.pipeIdx);
      const s6 = getPipeByIdx(slots[6]?.pipeIdx);

      const opMap={4:'opMachWrap4',3:'opMachWrap3',2:'opMachWrap2',5:'opMachWrap5',1:'opBendBadge1',6:'opBendBadge6'};
      [[4,s4],[3,s3],[2,s2],[5,s5],[1,s1],[6,s6]].forEach(([step,pipe])=>{
        const id = opMap[step];
        const el = document.getElementById(id);
        if(!el) return;
        const label = pipe ? `${pipe.id}` : '—';
        if(step===1 || step===6){
          el.textContent = `${step===1?'Bending M/C':'Bending M/C #2'} ${label!=='—'?'· '+label:''}`;
        }else{
          let tag = el.querySelector('.op-pipe-tag');
          if(!tag){
            tag = document.createElement('div');
            tag.className = 'op-pipe-tag';
            tag.style.cssText = 'position:absolute;top:4px;left:6px;font-size:8px;padding:2px 6px;border-radius:3px;background:rgba(8,16,30,.9);border:1px solid rgba(105,184,214,.4);color:#9ecfe3;z-index:3';
            el.appendChild(tag);
          }
          tag.textContent = label==='—'?'대기':label;
        }
      });

      const w4 = document.getElementById('wm4badge');
      if(w4 && s4){ w4.textContent = `Auto Loader · ${s4.id}`; }
      const wb1 = document.getElementById('wmBendBadge1');
      if(wb1){ wb1.textContent = `Bending M/C${s1?` · ${s1.id}`:''}`; }
      const wb6 = document.getElementById('wmBendBadge6');
      if(wb6){ wb6.textContent = `Bending M/C #2${s6?` · ${s6.id}`:''}`; }
    }

    let prevSig='';
    function reconcilePipeStates(){
      const slots=global.stageSlots||{};
      const occupied = new Set();
      Object.values(slots).forEach(slot=>{
        if(!slot) return;
        const p=getPipeByIdx(slot.pipeIdx);
        if(!p) return;
        occupied.add(p.id);
        if(p.status==='대기') setPipeStatusById(p.id, '진행중', Math.max(5,p.prog||0));
        if(typeof p.prog==='number' && p.prog < 95) setPipeStatusById(p.id, p.status, Math.max(5,p.prog+3));
      });

      const sig=JSON.stringify(Object.entries(slots).map(([k,v])=>[k,v?.pipeIdx??null]));
      if(prevSig && prevSig!==sig){
        const all=global.PIPE_DATA||[];
        all.forEach(p=>{
          if(!occupied.has(p.id) && p.status==='진행중'){
            // 밴딩 완료 후 슬롯에서 빠진 경우 완료 처리
            setPipeStatusById(p.id, '완료', 100);
          }
        });
      }
      prevSig=sig;
    }

    normalizeInitialPipeStates();
    const origRun=global.runPipelineTick;
    global.runPipelineTick=function(){
      // 기존 라인 스케줄러를 우선 사용해 연속 생산성을 유지
      const ret=origRun?.apply(this,arguments);
      // 보조 동기화: 선택된 프로젝트가 있으면 현재 선택 배관만 맞춘다(큐 소진 X)
      const p=nextPipeFromActiveProject();
      if(p && !global.processRunning){
        global.opSel=p;
      }
      reconcilePipeStates();
      syncStagePipeBadges();
      return ret;
    };

    const origUpdate=global.updateViewersForPipe;
    global.updateViewersForPipe=function(idx){
      // idx 기반으로 뷰어를 갱신해야 다수 배관 연속 표시가 유지된다.
      const pipe=(global.PIPE_DATA?.[idx % (global.PIPE_DATA?.length||1)]) || global.opSel;
      if(pipe) syncPipeSelection(pipe);
      return origUpdate?.apply(this,arguments);
    };

    const origSelOp=global.selOpPipe;
    global.selOpPipe=function(pipe){
      if(pipe?.id) ProjectStore.setPipeStatus(pipe.id,pipe.status||'대기');
      return origSelOp?.apply(this,arguments);
    };

    const origSelWorker=global.selWorkerPipe;
    global.selWorkerPipe=function(pipeId){
      const found=ProjectStore.findPipe(pipeId);
      if(found) global.workerSel=found.pipe;
      return origSelWorker?.apply(this,arguments);
    };

    global.PipelineEngine={HOLD_REASON,chooseBendingTarget,nextPipeFromActiveProject};
  }

  if(document.readyState==='loading') document.addEventListener('DOMContentLoaded',bind,{once:true});
  else bind();
})(window);
