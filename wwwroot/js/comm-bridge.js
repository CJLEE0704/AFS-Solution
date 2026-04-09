(function(global){
  const pending=[];
  function send(type,target,data){
    try{ global.sendToCSharp?.(type,target||'ALL',JSON.stringify(data||{})); }
    catch(e){ console.warn('[comm-bridge]',e); }
  }
  function reliableSend(type,target,data){
    const payload={type,target,data,attempt:0};
    pending.push(payload);
    flush();
  }
  function flush(){
    if(!pending.length) return;
    const item=pending[0];
    try{ send(item.type,item.target,item.data); pending.shift(); }
    catch(e){ item.attempt++; if(item.attempt>3) pending.shift(); }
  }
  global.CommBridge={send,reliableSend,flush};
})(window);
